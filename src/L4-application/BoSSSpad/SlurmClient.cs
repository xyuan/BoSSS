﻿/* =======================================================================
Copyright 2017 Technische Universitaet Darmstadt, Fachgebiet fuer Stroemungsdynamik (chair of fluid dynamics)

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Renci.SshNet;
using System.IO;

namespace BoSSS.Application.BoSSSpad {

    /// <summary>
    /// A <see cref="BatchProcessorClient"/> implementation for slurm systems on unix based hpc platforms
    /// </summary>
    public class SlurmClient : BatchProcessorClient {

        string m_Username;
        string m_Password;
        string m_ServerName;
        SshClient SSHConnection;

        public SlurmClient(string DeploymentBaseDirectory, string ServerName, string Username = null, string Password = null) {
            base.DeploymentBaseDirectory = DeploymentBaseDirectory;
            m_Username = Username;
            m_Password = Password;
            m_ServerName = ServerName;

            SSHConnection = new SshClient(m_ServerName, m_Username, m_Password);

            SSHConnection.Connect();
        }

        public override void EvaluateStatus(Job myJob, out int SubmitCount, out bool isRunning, out bool wasSuccessful, out bool isFailed, out string DeployDir) {
            string PrjName = InteractiveShell.WorkflowMgm.CurrentProject;
            DeployDir = null;
            isRunning = false;
            wasSuccessful = false;
            isFailed = false;

            var jobID = myJob.BatchProcessorIdentifierToken;

            var tempString = "squeue -j "+jobID;

            var squeue = SSHConnection.RunCommand(tempString);
            int tempcount = -1;
            //while (squeue_all.Result.Contains("\n")) { 

            //    tempcount++;
            //}

            SubmitCount = tempcount;
        }




        public override string GetStderrFile(Job myJob) {
            string fp = Path.Combine(myJob.DeploymentDirectory, "stderr.txt");
            if (File.Exists(fp)) {
                return fp;
            }
            else {
                return null;
            }

        }

        public override string GetStdoutFile(Job myJob) {
            string fp = Path.Combine(myJob.DeploymentDirectory, "stdout.txt");
            if (File.Exists(fp)) {
                return fp;
            }
            else {
                return null;
            }
        }

        public override object Submit(Job myJob) {
            throw new NotImplementedException();
        }
    }
}
