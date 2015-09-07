﻿namespace DbTestMonkey.Providers.SqlServer
{
   using System.Configuration;
   using DbTestMonkey.Contracts;

   public class ProviderConfiguration : ProviderConfigurationBase
   {
      [ConfigurationProperty("isLocalDbInstance", IsRequired = true, DefaultValue = true)]
      public bool IsLocalDbInstance
      {
         get
         {
            return (bool)this["isLocalDbInstance"];
         }
      }

      [ConfigurationProperty("connectionString", IsRequired = false)]
      public string ConnectionString
      {
         get
         {
            return (string)this["connectionString"];
         }
      }

      [ConfigurationProperty("localDbInstanceName", IsRequired = false)]
      public string LocalDbInstanceName
      {
         get
         {
            return (string)this["localDbInstanceName"];
         }
      }

      [ConfigurationProperty("databaseFileStorePath", IsRequired = true)]
      public string DatabaseFileStorePath
      {
         get
         {
            return (string)this["databaseFileStorePath"];
         }
      }

      [ConfigurationProperty("databases")]
      [ConfigurationCollection(typeof(SqlDatabaseConfiguration), AddItemName = "database")]
      public new SqlDatabasesConfigurationCollection Databases
      {
         get
         {
            return (SqlDatabasesConfigurationCollection)this["databases"];
         }
      }
   }
}