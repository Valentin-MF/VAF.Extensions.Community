﻿using MFiles.VaultApplications.Logging;
using MFilesAPI;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace MFiles.VAF.Extensions.Configuration.Upgrading.Rules
{
	public class EnsureLatestSerializationSettingsUpgradeRule<TSecureConfiguration>
		: SingleNamedValueItemUpgradeRuleBase
		where TSecureConfiguration : class, new()
	{
		private ILogger Logger { get; } = LogManager.GetLogger(typeof(EnsureLatestSerializationSettingsUpgradeRule<TSecureConfiguration>));


		public EnsureLatestSerializationSettingsUpgradeRule(ISingleNamedValueItem readFromAndWriteTo)
			: base(readFromAndWriteTo, Version.Parse("0.0"), Version.Parse("0.0"))
		{
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Returns the input; this class does no conversion.
		/// </remarks>
		protected override string Convert(string input)
			=> input;

		/// <inheritdoc />
		/// <remarks>
		/// Loads the data from the location in NVS, deserializes/deserializes, then updates NVS if the resulting data is different.
		/// Used in situations where the data being serialized has changed, so that the configuration editor reflects the correct data.
		/// </remarks>
		public override bool Execute(Vault vault)
		{
			// Sanity.
			if (null == NamedValueStorageManager)
				throw new InvalidOperationException($"{nameof(NamedValueStorageManager)} cannot be null.");
			if (null == JsonConvert)
				throw new InvalidOperationException($"{nameof(JsonConvert)} cannot be null.");

			// Read the existing data.
			// If we can't get the data then die.
			if (false == TryRead(ReadFrom, vault, out string data, out Version version))
			{
				Logger?.Debug($"Skipping ensuring latest serialization, as no data found in {ReadFrom}");
				return false;
			}

			// Serialize/deserialize then convert to JObject so that we can see what has changed.
			JObject oldData = JObject.Parse(data);
			JObject newData = JObject.Parse
			(
				JsonConvert.Serialize(JsonConvert.Deserialize<TSecureConfiguration>(oldData.ToString()))
			);

			// Copy across any comments.
			CopyComments(oldData, newData);

			// If the objects have not changed then stop.
			if (AreEqual(oldData, newData))
			{
				Logger?.Trace("Data in configuration already uses latest serialization; skipping conversion.");
				return true;
			}

			// We need to update.
			Logger?.Info($"Data in NVS at {ReadFrom.Namespace}.{ReadFrom.Name} ({ReadFrom.NamedValueType}) needed to be updated.");

			// Save the new data to storage.
			var type = WriteTo?.NamedValueType ?? ReadFrom.NamedValueType;
			var @namespace = WriteTo?.Namespace ?? ReadFrom.Namespace;
			var name = WriteTo?.Name ?? ReadFrom.Name;

			Logger?.Debug($"Attempting to update configuration in NVS.");
			{
				// Update the named values.
				Logger?.Trace($"Writing new configuration in {@namespace}.{name} ({type})...");
				var namedValues = NamedValueStorageManager.GetNamedValues(vault, type, @namespace) ?? new NamedValues();
				namedValues[name] = JsonConvert.Serialize(newData);
				NamedValueStorageManager.SetNamedValues(vault, type, @namespace, namedValues);
			}

			return true;

		}

		/// <summary>
		/// Returns <see langword="true"/> if the properties (and their values) in <paramref name="a"/> are the same as those in <paramref name="b"/>.
		/// </summary>
		/// <param name="a">The first property.</param>
		/// <param name="b">The second property.</param>
		/// <returns>
		/// <see langword="true"/> if <paramref name="a"/> and <paramref name="b"/> contain the same properties with the same values.
		/// Note that comment properties (names ending "-Comment") are not included in this comparison.
		/// Also note that null is equal to null but null is not equal to non-null.
		/// </returns>
		protected internal virtual bool AreEqual(JObject a, JObject b)
		{
			// Simple.
			if (a == null && b == null)
				return true;
			if (a == null || b == null)
				return false;

			// Compare property names.  Ignore comments.
			var aProperties = a.Properties().Select(p => p.Name).Where(n => !n.EndsWith("-Comment")).ToArray();
			var bProperties = b.Properties().Select(p => p.Name).Where(n => !n.EndsWith("-Comment")).ToArray();
			if (aProperties.Length != bProperties.Length)
				return false;

			// Check each in turn.
			foreach (var propertyName in aProperties)
			{
				// Sanity.
				var aPropertyValue = a[propertyName];
				var bPropertyValue = b[propertyName];
				if (aPropertyValue == null && bPropertyValue == null)
					return true;
				if (aPropertyValue == null || bPropertyValue == null)
					return false;
				if (a.Type != b.Type)
					return false;

				// Check each type.
				switch (aPropertyValue.Type)
				{
					case JTokenType.Object:
						if (false == AreEqual((JObject)aPropertyValue, (JObject)bPropertyValue))
							return false;
						break;
					case JTokenType.Array:
						{
							var aPropertyValueJArray = (JArray)aPropertyValue;
							var bPropertyValueJArray = (JArray)bPropertyValue;
							if (aPropertyValueJArray.Count != bPropertyValueJArray.Count)
								return false;
							// Does this need to be better?
							if (false == (aPropertyValueJArray.ToString() == bPropertyValueJArray.ToString()))
								return false;
						}
						break;
					default:
						if (false == (aPropertyValue.ToString() == bPropertyValue.ToString()))
							return false;
						break;
				}
			}

			// Everything was the same.
			return true;
		}
	}
}
