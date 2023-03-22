﻿using MFiles.VAF.AppTasks;
using MFiles.VAF.Configuration.AdminConfigurations;
using MFiles.VAF.Configuration.Domain.Dashboards;
using MFiles.VAF.Core;
using MFiles.VAF.Extensions.Dashboards;
using MFiles.VAF.Configuration.Logging;
using MFilesAPI;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MFiles.VAF.Configuration.Logging.NLog;
using System.Collections.Generic;
using System.Reflection;
using MFiles.VAF.Extensions.Dashboards.Commands;
using MFiles.VAF.Common;

namespace MFiles.VAF.Extensions
{
	internal class TaskManagerEx
	{
	}
	public partial class TaskManagerEx<TConfiguration>
		: TaskManager
		where TConfiguration : class, new()
	{
		private ILogger Logger { get; }
			= LogManager.GetLogger(typeof(TaskManagerEx));

		/// <summary>
		/// The vault application used to create this task manager.
		/// </summary>
		protected ConfigurableVaultApplicationBase<TConfiguration> VaultApplication { get; private set; }

		public TaskManagerEx
		(
			ConfigurableVaultApplicationBase<TConfiguration> vaultApplication,
			string id, 
			Vault permanentVault, 
			IVaultTransactionRunner transactionRunner,
			TimeSpan? processingInterval = null,
			uint maxConcurrency = 16,
			TimeSpan? maxLockWaitTime = null,
			TaskExceptionSettings exceptionSettings = null
		)
			: base(id, permanentVault, transactionRunner, processingInterval, maxConcurrency, maxLockWaitTime, exceptionSettings)
		{
			this.VaultApplication = vaultApplication
				?? throw new ArgumentNullException(nameof(vaultApplication));
			this.TaskEvent += TaskManagerEx_TaskEvent;
		}

		/// <inheritdoc />
		public new virtual string AddTask(Vault vault, string queueId, string taskType, TaskDirective directive = null, DateTime? activationTime = null)
		{
			this.Logger?.Info($"Adding task to queue {queueId} of type {taskType} at {(activationTime ?? DateTime.Now).ToString("O")}.");
			return base.AddTask(vault, queueId, taskType, directive, activationTime);
		}

		/// <summary>
		/// Cancels any future executions of tasks of type <paramref name="taskType"/> on queue <paramref name="queueId"/>.
		/// If <paramref name="scheduleFor"/> has a value then a new execution of the task is scheduled for this date/time.
		/// </summary>
		/// <param name="queueId">The queue that the task should be rescheduled on.</param>
		/// <param name="taskType">The task type to be rescheduled.</param>
		/// <param name="innerDirective">The inner directive to be passed to the rescheduled task.</param>
		/// <param name="vault">The vault reference to use for the operation.</param>
		/// <param name="scheduleFor">The date/time to schedule a new execution for.  If <see langword="null"/>, does not schedule a future execution.</param>
		/// <remarks>Adds an item to the scheduling queue, so that only one server performs this operation.</remarks>
		public virtual void RescheduleTask
		(
			string queueId,
			string taskType,
			TaskDirective innerDirective = null,
			Vault vault = null,
			DateTimeOffset? scheduleFor = null
		)
		{
			// Create the re-schedule directive.
			var directive = new RescheduleProcessorTaskDirective()
			{
				QueueID = queueId,
				TaskType = taskType,
				NextExecution = scheduleFor,
				InnerDirective = innerDirective
			};

			// What do we want to do?
			var action = new Action<Vault>((Vault v) =>
			{
				this.HandleReschedule(directive, v);
			});

			// Try and run the action.
			try
			{
				// If we have a transactional runner then run using that.
				var transactionRunner = this.VaultApplication?.GetTransactionRunner();
				if (null != transactionRunner)
					transactionRunner.Run((transactionalVault) => action(transactionalVault));
				else
					// Otherwise fall back to trying to run it outside of a transaction.
					action(vault ?? this.Vault);
			}
			catch
			{
				// If this fails for some transient issue then fall back to using the task approach to schedule it.
				// This might delay the task being added (e.g. if the task is not processed for a few seconds),
				// but it will also deal with retrying.
				this.AddTask
				(
					vault ?? this.Vault,
					this.VaultApplication.GetExtensionsSequentialQueueID(),
					this.VaultApplication.GetRescheduleTaskType(),
					directive
				);
			}
		}

		/// <summary>
		/// Registers/opens a queue with ID provided by <see cref="ConfigurableVaultApplicationBase{TSecureConfiguration}.GetExtensionsSequentialQueueID"/>
		/// and registers a process to handle tasks of type <see cref="ConfigurableVaultApplicationBase{TSecureConfiguration}.GetRescheduleTaskType"/>.
		/// </summary>
		/// <remarks>
		/// This is a sequential queue, and the <see cref="HandleReschedule(ITaskProcessingJob{RescheduleProcessorTaskDirective})"/>
		/// method processes the rescheduling tasks.
		/// </remarks>
		public virtual void RegisterExtensionsQueue()
		{
			// Register the scheduler queue.
			this.Logger?.Trace($"Registering scheduler queue {this.VaultApplication.GetExtensionsSequentialQueueID()}");
			this.RegisterQueue
			(
				this.VaultApplication.GetExtensionsSequentialQueueID(),
				new Processor[]
				{
					new TaskProcessor<RescheduleProcessorTaskDirective>
					(
						this.VaultApplication.GetRescheduleTaskType(),
						this.HandleReschedule,
						TransactionMode.Unsafe
					),
					new TaskProcessor<ImportReplicationPackageTaskDirective>
					(
						this.VaultApplication.GetReplicationPackageImportTaskType(),
						this.HandleReplicationPackageImport,
						TransactionMode.Unsafe
					)
				},
				MFTaskQueueProcessingBehavior.MFProcessingBehaviorSequential
			);
		}

		/// <summary>
		/// Handles a job for a replication package to be imported.
		/// </summary>
		/// <param name="job">The job to execute.</param>
		protected virtual void HandleReplicationPackageImport(ITaskProcessingJob<ImportReplicationPackageTaskDirective> job)
		{
			// Get the command.
			var command = this.VaultApplication
				.GetCommands(null)?
				.FirstOrDefault(c => c.ID == job?.Directive?.CommandId)
				as ImportReplicationPackageDashboardCommand<TConfiguration>;
			if (null == command)
			{
				this.Logger?.Warn($"Attempt to import package with command ID '{job?.Directive?.CommandId}', but command not found or command was incorrect type.");
				return;
			}

			// Run the command.
			// Note that the import must use a transactional vault reference,
			// but larger packages may take longer than a single transaction to run.
			// This is okay as the vault structural references will still be imported
			// even if the transaction times out.  As the import will also skip items
			// that are already imported, we can effectively import it multiple times
			// until it's done.
			job.Update(details: "Importing replication package...");
			var runner = this.VaultApplication.GetTransactionRunner();
			var complete = false;
			var attemptNumber = 1;
			while (!complete)
			{
				try
				{
					runner.Run((transactionalVault) =>
					{
						if (command.TryImport(transactionalVault))
						{
							this.Logger?.Info($"Imported replication package {command.ReplicationPackagePath}.");
							job.Update(details: "Replication package imported successfully.");
						}
						else
						{
							this.Logger?.Warn($"Failed to import replication package {command.ReplicationPackagePath}.");
							job.Update(details: "Failed to import replication package.");
						}
						complete = true;
					});
				}
				catch
				{
					// Let it re-run several times.
					if(++attemptNumber > command.MaximumImportAttempts)
					{
						complete = true;
						job.Update(details: "Import process cancelled due to repeated failures.");
					}
				}
			}
		}

		/// <summary>
		/// Cancels future executions of a task with a given queue ID and task type (read from the <paramref name="job"/>'s directive).
		/// If the directive also contains a next-execution date then reschedules an execution of the task at that time.
		/// </summary>
		/// <param name="job"></param>
		protected virtual void HandleReschedule(ITaskProcessingJob<RescheduleProcessorTaskDirective> job)
		{
			// Use the other overload.
			if (null != job.Directive)
				this.HandleReschedule(job.Directive);
		}

		/// <summary>
		/// Cancels future executions of a task with a given queue ID and task type (read from the <paramref name="job"/>'s directive).
		/// If the directive also contains a next-execution date then reschedules an execution of the task at that time.
		/// </summary>
		/// <param name="job"></param>
		protected virtual void HandleReschedule(RescheduleProcessorTaskDirective directive, Vault vault = null)
		{
			// Cancel any future executions.
			this.CancelAllFutureExecutions
			(
				directive.QueueID,
				directive.TaskType,
				includeCurrentlyExecuting: false,
				vault: vault ?? this.Vault
			);

			// Re-schedule?
			if (directive.NextExecution.HasValue)
				// Schedule the next run.
				this.AddTask
				(
					vault ?? this.Vault,
					directive.QueueID,
					directive.TaskType,
					directive: directive.InnerDirective,
					activationTime: new DateTime(directive.NextExecution.Value.ToUniversalTime().DateTime.Ticks, DateTimeKind.Utc)
				);
		}

		private void TaskManagerEx_TaskEvent(object sender, TaskManagerEventArgs e)
		{
			if (null == e.Queues || e.Queues.Count == 0)
				return;

			// When the job is finished, re-schedule.
			switch (e.EventType)
			{
				case TaskManagerEventType.TaskJobFinished:
					// Re-schedule as appropriate.
					switch (e.JobResult)
					{
						case TaskProcessingJobResult.Complete:
						case TaskProcessingJobResult.Fail:
						case TaskProcessingJobResult.Fatal:
							// Re-schedule.
							foreach (var t in e.Tasks)
							{
								// Are there any future executions scheduled?
								if (this.GetPendingExecutions<TaskDirective>(t.QueueID, t.TaskType, includeCurrentlyExecuting: false).Any())
									continue; // We already have one scheduled; don't re-schedule.

								// Can we get a next execution date for this task?
								var nextExecutionDate = this
									.VaultApplication?
									.RecurringOperationConfigurationManager?
									.GetNextTaskProcessorExecution(t.QueueID, t.TaskType);
								if (false == nextExecutionDate.HasValue)
									continue;

								// Schedule.
								this.Logger?.Info($"Re-scheduling {t.TaskType} on {t.QueueID} for {nextExecutionDate.Value.ToString("O")}");
								this.RescheduleTask(t.QueueID, t.TaskType, vault: this.Vault, scheduleFor: nextExecutionDate?.UtcDateTime);
							}
							break;
					}
					break;
			}
		}
	}
}
