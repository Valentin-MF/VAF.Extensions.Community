﻿using MFiles.VAF.Configuration;
using MFiles.VAF.Extensions.Configuration;
using MFiles.VAF.Extensions.Configuration.Upgrading;
using MFiles.VAF.Extensions.ExtensionMethods;
using MFiles.VAF.Extensions.Tests.ExtensionMethods;
using MFilesAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace MFiles.VAF.Extensions.Tests.Configuration.Upgrading.Rules
{
	[TestClass]
	public partial class EnsureLatestSerializationSettingsUpgradeRuleTests
		: TestBaseWithVaultMock
	{

		#region Proxy for easy testing

		internal class EnsureLatestSerializationSettingsUpgradeRuleProxy<TConfigurationType>
			: Extensions.Configuration.Upgrading.Rules.EnsureLatestSerializationSettingsUpgradeRule<TConfigurationType>
			where TConfigurationType : class, new()
		{
			internal Dictionary<MFNamedValueType, Dictionary<string, Dictionary<string, string>>> NamedValueStorage { get; }
				= new Dictionary<MFNamedValueType, Dictionary<string, Dictionary<string, string>>>();

			protected Mock<ISingleNamedValueItem> ReadAndWriteFrom { get; set; } = new Mock<ISingleNamedValueItem>();

			internal void SetReadWriteLocation(MFNamedValueType type, string @namespace, string @name)
			{
				this.ReadFrom.NamedValueType = type;
				this.ReadFrom.Namespace = @namespace;
				this.ReadFrom.Name = @name;
			}
			internal void GetReadWriteLocation(out MFNamedValueType type, out string @namespace, out string @name)
			{
				type = this.ReadFrom.NamedValueType;
				@namespace = this.ReadFrom.Namespace;
				@name = this.ReadFrom.Name;
			}
			internal void SetReadWriteLocationValue(Vault vault, string value)
			{
				this.NamedValueStorageManager.SetValue(vault, this.ReadFrom.NamedValueType, this.ReadFrom.Namespace, this.ReadFrom.Name, value);
			}
			internal string GetReadWriteLocationValue(Vault vault)
			{
				return this.NamedValueStorageManager.GetValue(vault, this.ReadFrom.NamedValueType, this.ReadFrom.Namespace, this.ReadFrom.Name);
			}

			public EnsureLatestSerializationSettingsUpgradeRuleProxy()
				// We have to pass a dummy location through, but we override it in a minute.
				: base(Mock.Of<ISingleNamedValueItem>(m => m.IsValid() == true))
			{
				this.ReadAndWriteFrom.SetupAllProperties();
				this.ReadAndWriteFrom.Setup(m => m.IsValid()).Returns(true);
				this.ReadFrom = this.ReadAndWriteFrom.Object;

				var nvsManagerMock = new Mock<INamedValueStorageManager>();
				nvsManagerMock.Setup(m => m.GetNamedValues(It.IsAny<Vault>(), It.IsAny<MFNamedValueType>(), It.IsAny<string>()))
					.Returns((Vault vault, MFNamedValueType t, string n) =>
					{
						var namedValues = new NamedValues();

						if (this.NamedValueStorage.TryGetValue(t, out Dictionary<string, Dictionary<string, string>> d))
						{
							if (d?.TryGetValue(n, out Dictionary<string, string> values) ?? false
								&& null != values)
							{
								foreach (var kvp in values)
								{
									namedValues[kvp.Key] = kvp.Value;
								}
							}
						}

						return namedValues;
					});
				nvsManagerMock.Setup(m => m.SetNamedValues(It.IsAny<Vault>(), It.IsAny<MFNamedValueType>(), It.IsAny<string>(), It.IsAny<NamedValues>()))
					.Callback((Vault vault, MFNamedValueType t, string n, NamedValues values) =>
					{
						values = values ?? new NamedValues();

						if (!this.NamedValueStorage.TryGetValue(t, out Dictionary<string, Dictionary<string, string>> d))
						{
							d = new Dictionary<string, Dictionary<string, string>>();
							this.NamedValueStorage.Add(t, d);
						}
						if(!d.TryGetValue(n, out Dictionary<string, string> v))
						{
							v = new Dictionary<string, string>();
							d.Add(n, v);
						}

						v.Clear();
						foreach(string key in values.Names)
						{
							v.Add(key, values[key]?.ToString());
						}

					});

				this.NamedValueStorageManager = nvsManagerMock.Object;
			}

		}
		internal class EnsureLatestSerializationSettingsUpgradeRuleProxy
			: EnsureLatestSerializationSettingsUpgradeRuleProxy<EnsureLatestSerializationSettingsUpgradeRuleProxy.MyConfiguration>
		{

			public class MyConfiguration
			{

			}

		}

		#endregion

		[TestMethod]
		public void EnsureJsonConfEditorDefaultPropertiesAreNotSerialized()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<ConfigurationWithJsonConfEditorDefaultValue>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, "{}");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson("{}", rule.GetReadWriteLocationValue(vault));

		}

		[DataContract]
		private class ConfigurationWithJsonConfEditorDefaultValue
		{
			[DataMember]
			[JsonConfEditor(DefaultValue = "hello")]
			public string Hello { get; set; } = "hello";
		}

		[TestMethod]
		public void EnsureDefaultPropertiesAreNotSerialized()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<ConfigurationWithDefaultPropertyValue>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, "{}");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson("{}", rule.GetReadWriteLocationValue(vault));

		}

		[DataContract]
		private class ConfigurationWithDefaultPropertyValue
		{
			[DataMember]
			public string Hello { get; set; } = "hello";
		}

		[TestMethod]
		public void EnsureDefaultValuePropertiesAreNotSerialized()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<ConfigurationWithDefaultValuePropertyValue>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, "{}");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson("{}", rule.GetReadWriteLocationValue(vault));

		}

		[DataContract]
		private class ConfigurationWithDefaultValuePropertyValue
		{
			[DefaultValue("hello")]
			[DataMember]
			public string Hello { get; set; } = "hello";
		}

		[TestMethod]
		public void EnsureDefaultFieldsAreNotSerialized()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<ConfigurationWithDefaultFieldValue>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, "{}");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson("{}", rule.GetReadWriteLocationValue(vault));

		}

		[DataContract]
		private class ConfigurationWithDefaultFieldValue
		{
			[DataMember]
			public string Hello = "hello";
		}

		[TestMethod]
		public void EnsureDefaultPropertiesAreNotSerialized_SetInConstructor()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<ConfigurationWithDefaultPropertyValueSetInConstructor>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, "{}");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson("{}", rule.GetReadWriteLocationValue(vault));

		}

		[DataContract]
		private class ConfigurationWithDefaultPropertyValueSetInConstructor
		{
			[DataMember]
			public string Hello { get; set; }
			public ConfigurationWithDefaultPropertyValueSetInConstructor()
			{
				this.Hello = "world";
			}
		}

		[TestMethod]
		public void EnsureDefaultPropertiesAreNotSerialized_SetInConstructorDerived()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<ConfigurationWithDefaultPropertyValueSetInConstructorDerived>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, "{}");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson("{}", rule.GetReadWriteLocationValue(vault));

		}

		[TestMethod]
		public void EnsureDefaultPropertiesAreNotSerialized_SetInConstructorDerived_CustomValues()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<ConfigurationWithDefaultPropertyValueSetInConstructorDerived>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, @"{ 
				""Hello"" : ""User""
			}");

			// This should not touch the original, as it's the same.
			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson(@"{ 
				""Hello"" : ""User""
			}", rule.GetReadWriteLocationValue(vault));

		}

		[DataContract]
		private abstract class ConfigurationWithDefaultPropertyValueSetInConstructor_Abstract
		{
			[DataMember]
			public string Hello { get; set; }
			public ConfigurationWithDefaultPropertyValueSetInConstructor_Abstract()
			{
				this.Hello = "world";
			}
		}

		[DataContract]
		private class ConfigurationWithDefaultPropertyValueSetInConstructorDerived
			: ConfigurationWithDefaultPropertyValueSetInConstructor_Abstract
		{
			[DataMember]
			public string World { get; set; }
			public ConfigurationWithDefaultPropertyValueSetInConstructorDerived()
				: base()
			{
				this.World = "hello";
			}
		}

		[TestMethod]
		public void EnsureDefaultPropertiesAreNotSerialized_WithDefaultSubObject()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<ConfigurationWithDefaultPropertyValueSetInConstructorWithSubObject>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, @"{ ""Sub"" : { ""Hello"" : ""hello"" } }");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson("{}", rule.GetReadWriteLocationValue(vault));

		}

		[TestMethod]
		public void EnsureDefaultPropertiesAreNotSerialized_WithSubObject()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<ConfigurationWithDefaultPropertyValueSetInConstructorWithSubObject>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, @"{ ""Sub"" : { ""Hello"" : ""to you"" } }");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson(@"{ ""Sub"" : { ""Hello"" : ""to you"" } }", rule.GetReadWriteLocationValue(vault));

		}

		[TestMethod]
		public void EnsureDefaultPropertiesAreNotSerialized_WithEmptySubObject()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<ConfigurationWithDefaultPropertyValueSetInConstructorWithSubObject>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, @"{}");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson("{}", rule.GetReadWriteLocationValue(vault));

		}

		[DataContract]
		private class ConfigurationWithDefaultPropertyValueSetInConstructorWithSubObject
			: ConfigurationWithDefaultPropertyValueSetInConstructorDerived
		{
			[DataMember]
			public ConfigurationWithDefaultFieldValue Sub { get; set; }
			public ConfigurationWithDefaultPropertyValueSetInConstructorWithSubObject()
				: base()
			{
				this.Sub = new ConfigurationWithDefaultFieldValue();
			}
		}

		[TestMethod]
		public void EnsureDefaultPropertiesAreNotSerialized_WithNewDefaultPropertyValue()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<ConfigurationWithDefaultPropertyValueSetInConstructorWithSubObject>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, @"{}");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson("{}", rule.GetReadWriteLocationValue(vault));

		}

		[DataContract]
		private class ConfigurationWithNewDefaultValuePropertyValue
			: ConfigurationWithDefaultValuePropertyValue
		{
			[DefaultValue("world")]
			[DataMember]
			public new string Hello { get; set; } = "world";
		}

		[TestMethod]
		public void EnsureDefaultPropertiesAreNotSerialized_WithOverriddenDefaultPropertyValue()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<ConfigurationWithDefaultPropertyValueSetInConstructorWithSubObject>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, @"{}");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson("{}", rule.GetReadWriteLocationValue(vault));

		}

		[DataContract]
		private class ConfigurationWithVirtualValuePropertyValue
		{
			[DefaultValue("world")]
			[DataMember]
			public virtual string Hello { get; set; } = "world";
		}

		[DataContract]
		private class ConfigurationWithOverriddenValuePropertyValue
			: ConfigurationWithVirtualValuePropertyValue
		{
			[DefaultValue("a")]
			[DataMember]
			public new string Hello { get; set; } = "a";
		}

		[TestMethod]
		public void LoggingUpgrade_Empty()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<NLogLoggingConfiguration>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, @"{}");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson("{}", rule.GetReadWriteLocationValue(vault));

		}

		[TestMethod]
		public void LoggingUpgrade_WithAdvanced()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<NLogLoggingConfiguration>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, @"{
				""Enabled"": true,
				""DefaultTargetConfiguration"": {
					""Enabled"": true,
					""Advanced"": {
						""ConcurrentWrites"": true
					}
				}
			}");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson(@"{
				""Enabled"": true,
				""DefaultTargetConfiguration"": {
					""Enabled"": true,
					""Advanced"": {
						""ConcurrentWrites"": true
					}
				}
			}", rule.GetReadWriteLocationValue(vault));

		}

		[TestMethod]
		public void LoggingUpgrade_WithArraysAndAdvanced()
		{
			var vault = Mock.Of<Vault>();
			var rule = new EnsureLatestSerializationSettingsUpgradeRuleProxy<NLogLoggingConfiguration>();
			rule.SetReadWriteLocation(MFNamedValueType.MFConfigurationValue, "sampleNamespace", "config");
			rule.SetReadWriteLocationValue(vault, @"{
				""Enabled"": true,
				""DefaultTargetConfiguration"": {
					""Enabled"": true,
					""Advanced"": {
						""ConcurrentWrites"": true
					}
				},
				""FileTargetConfigurations"": [
					{
						""Name"": ""asd"",
						""Enabled"": true,
						""FileRotation"": {
							""MaximumArchiveDays"": 21
						},
						""Advanced"": {
							""ConcurrentWrites"": true
						}
					}
				]
			}");

			Assert.IsTrue(rule.Execute(vault));
			Assert.That.AreEqualJson(@"{
				""Enabled"": true,
				""DefaultTargetConfiguration"": {
					""Enabled"": true,
					""Advanced"": {
						""ConcurrentWrites"": true
					}
				},
				""FileTargetConfigurations"": [
					{
						""Name"": ""asd"",
						""Enabled"": true,
						""FileRotation"": {
							""MaximumArchiveDays"": 21
						},
						""Advanced"": {
							""ConcurrentWrites"": true
						}
					}
				]
			}", rule.GetReadWriteLocationValue(vault));

		}
	}
}
