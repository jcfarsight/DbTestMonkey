﻿namespace DbTestMonkey.Providers.SqlServer
{
   using System;
   using System.Collections.Generic;
   using System.Configuration;
   using System.Data;
   using System.Data.SqlClient;
   using System.Diagnostics;
   using System.IO;
   using System.Linq;
   using System.Text.RegularExpressions;
   using Microsoft.SqlServer.Dac;

   public class DacManager
   {
      private const string ConfigurationSectionName = "sqlServer";

      private readonly Func<IDbConnection> _connectionFactory;

      private readonly Action<string> _logAction;

      public DacManager(Func<IDbConnection> connectionFactory, Action<string> logAction)
      {
         _connectionFactory = connectionFactory;
         _logAction = logAction ?? new Action<string>(message => { });
      }

      public void DeployDacPac(string databaseName)
      {
         ProviderConfiguration config =
            (ProviderConfiguration)ConfigurationManager.GetSection("dbTestMonkey/" + ConfigurationSectionName);

         SqlDatabaseConfiguration databaseConfiguration = config.Databases[databaseName];

         string dacpacPath = databaseConfiguration.DacPacFilePath;

         _logAction("Loading Dacpac into memory");
         Stopwatch totalTimer = Stopwatch.StartNew();
         Stopwatch loadPackageTimer = Stopwatch.StartNew();
         _logAction("current directory:" + Environment.CurrentDirectory);
         _logAction("dacpacPath:" + dacpacPath);

         using (DacPackage dacPackage = DacPackage.Load(dacpacPath, DacSchemaModelStorageType.Memory, FileAccess.Read))
         {
            databaseName = databaseName ?? dacPackage.Name;

            loadPackageTimer.Stop();
            _logAction("Package loaded, initialising DacServices");

            using (IDbConnection connection = _connectionFactory())
            {
               try
               {
                  connection.ChangeDatabase(databaseName);
               }
               catch
               {
                  _logAction(
                     "Could not change connection to database " + 
                     databaseName + 
                     " before pre-deployment script. Database may not yet exist.");
               }

               // Execute the DAC pre-deployment script.
               if (dacPackage.PreDeploymentScript != null)
               {
                  using (IDbCommand command = connection.CreateCommand())
                  {
                     command.CommandText = new StreamReader(dacPackage.PreDeploymentScript).ReadToEnd();
                     command.CommandText = command.CommandText.Replace("\nGO", "");
                     command.ExecuteNonQuery();
                  }
               }

               _logAction("Deploying dacpac");
               Stopwatch dacpacDeployTimer = Stopwatch.StartNew();

               DacDeployOptions options = new DacDeployOptions()
               {
                  CreateNewDatabase = true
               };

               Stopwatch dacpacServiceTimer = Stopwatch.StartNew();
               DacServices dacServices = new DacServices(connection.ConnectionString);
               dacpacServiceTimer.Stop();

               _logAction("DacServices initialisation took " + dacpacServiceTimer.ElapsedMilliseconds + " ms");

               dacServices.Message += dacServices_Message;
               dacServices.ProgressChanged += dacServices_ProgressChanged;

               dacServices.Deploy(dacPackage, databaseName, upgradeExisting: true, options: options);

               dacpacDeployTimer.Stop();

               _logAction(
                  "Deploying dacpac took " + 
                  dacpacDeployTimer.ElapsedMilliseconds + 
                  " ms");

               // If the user has opted to only run the post-deployment script after the DACPAC
               // deployment and not per-test, it needs to run once.
               if (!config.Databases[databaseName].ExecutePostDeploymentScriptPerTest)
               {
                  ExecutePostDeploymentScript(databaseName, dacPackage);
               }
            }
         }

         totalTimer.Stop();
         _logAction("Total dacpac time was " + totalTimer.ElapsedMilliseconds + " ms");
      }

      /// <summary>
      /// Round Robin approach to creating tables. This is pretty hacky but it should serve the purpose.
      /// Any tables that fail due to FK constraints or otherwise will be re-tried again next round so long
      /// as at least one table creation suceeded this time. This will ensure an infinite loop until stack
      /// overflow does not occur.
      /// </summary>
      /// <param name="command">The command to use when executing queries.</param>
      /// <param name="tableCreationQueries">A list of the queries that need to be tried this round.</param>
      private void RecursiveTryCreateTables(IDbCommand command, IEnumerable<string> tableCreationQueries)
      {
         _logAction(
            "Performing a round of table creation scripts. Tables remaining: " + tableCreationQueries.Count());

         List<string> failedTableCreationQueries = new List<string>();

         bool wasAtLeastOneCreationSuccessful = false;

         foreach (string scriptPart in tableCreationQueries)
         {
            try
            {
               command.CommandText = scriptPart;
               command.ExecuteNonQuery();

               wasAtLeastOneCreationSuccessful = true;
            }
            catch
            {
               failedTableCreationQueries.Add(scriptPart);
            }
         }

         if (wasAtLeastOneCreationSuccessful)
         {
            RecursiveTryCreateTables(command, failedTableCreationQueries);
         }
         else if (failedTableCreationQueries.Any())
         {
            _logAction("One or more tables failed to create after multiple recursive attempts. Ensure script is correct and try again.");
            failedTableCreationQueries.ForEach(fq => _logAction("FAILED: " + fq));
         }
      }

      public void ExecutePostDeploymentScript(string databaseName)
      {
         ProviderConfiguration config =
            (ProviderConfiguration)ConfigurationManager.GetSection("dbTestMonkey/" + ConfigurationSectionName);

         string dacpacPath = ((SqlDatabaseConfiguration)config.Databases[databaseName]).DacPacFilePath;

         _logAction("Loading Dacpac into memory");
         _logAction("current directory:" + Environment.CurrentDirectory);
         _logAction("dacpacPath:" + dacpacPath);

         using (DacPackage dacPackage = DacPackage.Load(dacpacPath, DacSchemaModelStorageType.Memory, FileAccess.Read))
         {
            ExecutePostDeploymentScript(databaseName, dacPackage);
         }
      }

      private void ExecutePostDeploymentScript(string databaseName, DacPackage dacPackage)
      {
         databaseName = databaseName ?? dacPackage.Name;

         using (IDbConnection connection = _connectionFactory())
         {
            try
            {
               connection.ChangeDatabase(databaseName);
            }
            catch
            {
               _logAction(
                  "Could not change connection to database " +
                  databaseName +
                  " before post-deployment script. Database may not yet exist.");
            }

            // Execute the DAC post-deployment script.
            if (dacPackage.PostDeploymentScript != null)
            {
               using (IDbCommand command = connection.CreateCommand())
               {
                  command.CommandText = new StreamReader(dacPackage.PostDeploymentScript).ReadToEnd();
                  command.CommandText = command.CommandText.Replace("\nGO", "");
                  command.ExecuteNonQuery();
               }
            }
         }
      }

      private void dacServices_ProgressChanged(object sender, DacProgressEventArgs e)
      {
         _logAction(e.Message);
      }

      private void dacServices_Message(object sender, DacMessageEventArgs e)
      {
         _logAction(e.Message.MessageType.ToString() + ": " + e.Message.Message);
      }
   }
}