using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Connectivity.WebServices;
using Autodesk.Connectivity.WebServicesTools;
using System.Xml;
using System.Data.SQLite;
using System.Threading;
using VDF = Autodesk.DataManagement.Client.Framework;

namespace queueBomFixJobs
{
    class Program
    {

        static string sourceCheckerDB = "queueDB.sdf";
        static SQLiteConnection m_dbConnection;
        static WebServiceManager _webSvc = null;
        static int maxQueuedJobs = Properties.Settings.Default.MaxJobsInQueue;
        static int jobPrio = Properties.Settings.Default.JobPriority;

        static void GetAllVaultInventorFiles()
        {
            Console.WriteLine("Collecting Vault Inventor files...");
            var propDefs = _webSvc.PropertyService.GetPropertyDefinitionsByEntityClassId("FILE");
            var provider = propDefs.FirstOrDefault(p => p.SysName.ToLower().Equals("provider"));
            var fileExt = propDefs.FirstOrDefault(p => p.SysName.ToLower().Equals("extension"));

            var srchCond = new Autodesk.Connectivity.WebServices.SrchCond();
            srchCond.PropDefId = provider.Id;
            srchCond.SrchOper = 3;
            srchCond.SrchRule = SearchRuleType.Must;
            srchCond.PropTyp = PropertySearchType.SingleProperty;
            srchCond.SrchTxt = "Inventor";
            var srchCond2 = new Autodesk.Connectivity.WebServices.SrchCond();
            srchCond2.PropDefId = fileExt.Id;
            srchCond2.SrchOper = 2;
            srchCond2.SrchRule = SearchRuleType.Must;
            srchCond2.PropTyp = PropertySearchType.SingleProperty;
            srchCond2.SrchTxt = "idw";
            var srchStatus = new Autodesk.Connectivity.WebServices.SrchStatus();
            string bookmark = String.Empty;
            List<long> masterIds = new List<long>();
            long counter = 0;
            do
            {
                var files = _webSvc.DocumentService.FindFilesBySearchConditions(new SrchCond[] { srchCond, srchCond2 }, null, null, true, true, ref bookmark, out srchStatus);
                counter += files.Count();
                Console.WriteLine(String.Format("Files found: {0}/{1}, adding to database ...", counter, srchStatus.TotalHits));
                List<string> transaction = new List<string>();
                foreach (var file in files)
                {
                    if (!file.IsOnSite) continue;
                    if (masterIds.Contains(file.MasterId)) continue;
                    transaction.Add(String.Format("INSERT INTO files (MasterID, Name, HasBOM, JobQueued,Processed, ID) VALUES ('{0}','{1}',0,0,0,'{2}');", file.MasterId, file.Name.Replace("'","''"), file.Id));
                    masterIds.Add(file.MasterId);
                }
                string sql = String.Join(Environment.NewLine, transaction);
                sql = "BEGIN TRANSACTION;" + Environment.NewLine + sql + Environment.NewLine + "COMMIT;";
                //System.IO.File.WriteAllText(String.Format("transaction_{0}.txt", DateTime.Now.ToString("yyyyMMddHHmmss")), sql);
                var command = new SQLiteCommand(sql, m_dbConnection);
                command.ExecuteNonQuery();
            } while (counter < srchStatus.TotalHits);
        }
        
        static void OpenDbConnection(string dbPath)
        {
            Console.WriteLine(String.Format("Opening BCP database: '{0}'",dbPath));
            m_dbConnection =  new SQLiteConnection(String.Format("Data Source={0}",dbPath));
            m_dbConnection.Open();
            string sql = "CREATE TABLE IF NOT EXISTS[files](MasterID string PRIMARY KEY, ID string, Name Nvarchar(1000), HasBOM bool, JobQueued bool, Processed bool)";
            SQLiteCommand command = new SQLiteCommand(sql, m_dbConnection);
            command.ExecuteNonQuery();
       }

        static List<dbFile> GetAllFilesFromDB()
        {
            string sql = String.Format("SELECT * FROM files");
            var command = new SQLiteCommand(sql, m_dbConnection);
            SQLiteDataReader reader = command.ExecuteReader();
            List<dbFile> files = new List<dbFile>();
            while (reader.Read())
                files.Add(new dbFile() { MasterId = reader["MasterID"].ToString(), Id = reader["ID"].ToString(), HasBOM = bool.Parse(reader["HasBOM"].ToString()), JobQueued= bool.Parse(reader["JobQueued"].ToString()), Processed = bool.Parse(reader["Processed"].ToString()), Name= reader["Name"].ToString() });
            return files;
        }

        class dbFile
        {
            public string MasterId { get; set; }
            public string Id { get; set; }
            public string Name { get; set; }
            public bool HasBOM { get; set; }
            public bool JobQueued { get; set; }
            public bool Processed { get; set; }
        }

        static void CloseDbConnection()
       {
           Console.WriteLine("Closing BCP database");
           m_dbConnection.Close();
       }

        static void WaitIfTooManyJobs()
        {
            var jobs = _webSvc.JobService.GetJobsByDate(100000, DateTime.MinValue);
            if (jobs == null) return;
            var bomFixJobs = jobs.Where(j => j.Typ.ToLower().Equals("autodesk.vault.extractbom.inventor") && j.StatusCode == JobStatus.Ready).ToArray();
            int sleepcounter = 0;
            while (bomFixJobs.Count() > maxQueuedJobs)
            {
                Console.WriteLine(String.Format("{0} pending BomFix jobs - {1} total jobs in the queue, waiting {3} seconds [{2}]...", bomFixJobs.Count(), jobs.Count(), sleepcounter++, Properties.Settings.Default.IdleTimeInSeconds));
                Thread.Sleep(Properties.Settings.Default.IdleTimeInSeconds*1000);
                jobs = _webSvc.JobService.GetJobsByDate(100000, DateTime.MinValue);
                if (jobs == null) return;
                bomFixJobs = jobs.Where(j => j.Typ.ToLower().Equals("autodesk.vault.extractbom.inventor") && j.StatusCode == JobStatus.Ready).ToArray();
            }
        }

       static void Main(string[] args)
       {
            Console.WriteLine("Connecting to Vault...");
            VDF.Vault.Currency.Connections.Connection connection = VDF.Vault.Forms.Library.Login(null);
            if (connection == null) return;
            _webSvc = connection.WebServiceManager;
            OpenDbConnection(sourceCheckerDB);
            
            List<dbFile> files = GetAllFilesFromDB();
            if (files.Count == 0)
            {
                GetAllVaultInventorFiles();
                files = GetAllFilesFromDB();
            }
            Console.WriteLine(String.Format("{0} files found in database", files.Count()));
            string sql = "";
            SQLiteCommand command = null;
            var unprocessedFiles = files.Where(f => !f.Processed).ToList();
            long counter = 0;
            Console.WriteLine(String.Format("({0}) files to check for missing BOMs...",unprocessedFiles.Count));
            foreach (var file in unprocessedFiles)
            {
                counter++;
                var f = _webSvc.DocumentService.GetLatestFileByMasterId(long.Parse(file.MasterId));
                var bom = _webSvc.DocumentService.GetBOMByFileId(f.Id);
                string HasBOM = "1";
                if (bom == null) HasBOM = "0";
                sql = String.Format("UPDATE files SET Processed=1, HasBOM={1} WHERE MasterID={0}", file.MasterId, HasBOM);
                command = new SQLiteCommand(sql, m_dbConnection);
                command.ExecuteNonQuery();
                Console.Write(String.Format("\r[{0}/{1}] {2}={3}",counter,unprocessedFiles.Count,file.Name,HasBOM));
            }
            files = GetAllFilesFromDB();
            var toBeQueued = files.Where(f => f.Processed && !f.HasBOM && !f.JobQueued).ToList();
            Console.WriteLine();
            Console.WriteLine(String.Format("{0} jobs to be queued...",toBeQueued.Count));
            counter = 1;
            foreach(var file in toBeQueued) {
                Console.WriteLine(String.Format("[{1}/{2}] queueing job for {0}",file.Name,counter++,toBeQueued.Count));
                WaitIfTooManyJobs();
                try
                {
                    _webSvc.JobService.AddJob("autodesk.vault.extractbom.inventor", String.Format("BOM Fix for {0}", file.Name), new JobParam[] { new JobParam() { Name = "EntityClassId", Val = "File" }, new JobParam() { Name = "FileMasterId", Val = file.MasterId.ToString() } }, jobPrio);
                    sql = String.Format("UPDATE files SET JobQueued=1 WHERE MasterID={0}", file.MasterId);
                    command = new SQLiteCommand(sql, m_dbConnection);
                    command.ExecuteNonQuery();
                }
                catch {
                }
            }
            CloseDbConnection();
            Console.WriteLine("DONE! press a key in order to close this application ");
            Console.ReadKey();
        }
    }

    
}
