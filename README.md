# queue-extractbom-job

[![Windows](https://img.shields.io/badge/Platform-Windows-lightgray.svg)](https://www.microsoft.com/en-us/windows/)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.7-blue.svg)](https://dotnet.microsoft.com/)
[![Vault](https://img.shields.io/badge/Autodesk%20Vault-2019-yellow.svg)](https://www.autodesk.com/products/vault/)

# Description

If you just made an import via BCP and your Inventor file have no BOM data, then operations like assign items will fail. Since Vault 2017, there is a job called 'autodesk.vault.extractbom.inventor', which extracts the BOM data and stores it into Vault. By running this job over your files, the BOM data will be created, without creating a new version/revision.

In order to queue the jobs in a 'smart' way, we created the queueBomFixJob tool, which goes over your Vault, looks for all Inventor files, checks whether the BOM object exists or not, and if not, it queues a job. In order to prevent the jobqueue to be filled up too much and slow down Vault, this tool will queue just x (MaxJobsInQueue) jobs at the time with priority y (JobPriority) and then go to sleep for z (IdleTimeInSeconds) seconds. The parameter are configurable in the .config file.

The tool can be closed and restarted at any time, as it stored his "to do" list in a local little database. The first time you start the tool, it collects the data from Vault and stores them into his database. The next time you start the tool, it will start from where he left off. If you like to have a clean rerun, then just delete the file 'queueDB.sdf', located in the same folder as the executable. Doing so, you remove the 'memory' of the tool, and he will start from scratch with investigating and queueing.

## Author
coolOrange s.r.l.  
Project Development Team

![coolOrange](https://user-images.githubusercontent.com/36075173/46519882-4b518880-c87a-11e8-8dab-dffe826a9630.png)
