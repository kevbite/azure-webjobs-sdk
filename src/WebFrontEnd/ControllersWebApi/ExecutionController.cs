﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using DaasEndpoints;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using WebFrontEnd.Controllers;
using RunnerHost;
using WebFrontEnd.Models.Protocol;

namespace WebFrontEnd
{
    public class ExecutionController : ApiController
    {
        private readonly Services _services;
        private readonly IFunctionTableLookup _functionTableLookup;
        internal ExecutionController(Services services, IFunctionTableLookup functionTableLookup)
        {
            _services = services;
            _functionTableLookup = functionTableLookup;
        }

        private Services GetServices()
        {
            return _services;
        }

        // caller should throw the exception so we analyze control flow
        private Exception NewUserError(string format, params string[] args)
        {
            string msg = string.Format(format, args);
            var response = this.Request.CreateErrorResponse(HttpStatusCode.BadRequest, msg);
            return new HttpResponseException(response);
        }

        [HttpGet]
        public void Heartbeat()
        {
            // Lets tooling verify they have a valid service URL.
        }

        [HttpPost]
        public void Scan(string func, string container)
        {
            FunctionDefinition f = _functionTableLookup.Lookup(func);
            if (f == null)
            {
                throw NewUserError("Function not found. Do you need to add it to the index? '{0}'", func);
            }

            var account = f.GetAccount();
            Helpers.ScanBlobDir(GetServices(), account, new CloudBlobPath(container));
        }

        // Execute the given function.
        // Assumes execution is used via named parameters.
        [HttpPost]
        public BeginRunResult Run(string func, [FromBody] Guid[] prereqs)
        {
            FunctionDefinition f = _functionTableLookup.Lookup(func);
            if (f == null)
            {
                throw NewUserError("Function not found. Do you need to add it to the index? '{0}'", func);
            }

            // Get query parameters
            var uri = this.Request.RequestUri;
            Dictionary<string, string> parameters = GetParamsFromQuery(uri);
            parameters.Remove("func"); //  remove query parameter that we added for the function id

            FunctionInstanceGuid parentGuid = CallUtil.GetParentGuid(parameters);

            // Bind and queue.
            // Queue could be an hour deep
            try
            {
                var instance = Orchestrator.Worker.GetFunctionInvocation(f, parameters, prereqs);
                instance.TriggerReason = new InvokeTriggerReason
                {
                    Message = "Explicitly invoked via POST WebAPI.",
                    ParentGuid = parentGuid
                };

                IQueueFunction executor = GetServices().GetQueueFunction();
                ExecutionInstanceLogEntity result = executor.Queue(instance);

                return new BeginRunResult { Instance = result.FunctionInstance.Id };
            }
            catch (InvalidOperationException ex)
            {
                throw NewUserError(ex.Message);
            }
        }

        [HttpGet]
        public FunctionInstanceStatusResult GetStatus(Guid id)
        {
            IFunctionInstanceLookup logger = GetServices().GetFunctionInstanceQuery();
            var instance = logger.Lookup(id);
            return new FunctionInstanceStatusResult
            {
                Status = (FunctionInstanceStatusModel)instance.GetStatus(),
                OutputUrl = instance.OutputUrl,
                ExceptionMessage = instance.ExceptionMessage,
                ExceptionType = instance.ExceptionType,
            };
        }

        private static Dictionary<string, string> GetParamsFromQuery(Uri uri)
        {
            NameValueCollection nvc = uri.ParseQueryString();
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            foreach (var key in nvc.AllKeys)
            {
                parameters[key] = nvc[key];
            }
            return parameters;
        }

        [HttpPost]
        public RegisterArgsResponse RegisterFunction(RegisterArgs args)
        {
            var model = RegisterFuncSubmitworker(new IndexOperation
            {
                UserAccountConnectionString = args.AccountConnectionString,
                Blobpath = args.ContainerName
            });
            return new RegisterArgsResponse { ResultUri = model.Writeback.ToString() };
        }

        public class BeginRunResult
        {
            public Guid Instance { get; set; }
        }

        // $$$ Should we just send back ExecutionInstanceLogEntity?
        public class FunctionInstanceStatusResult
        {
            public FunctionInstanceStatusModel Status { get; set; }

            // For retrieving the console output.
            // This is incrementally updated.
            public string OutputUrl { get; set; }

            // For failures
            public string ExceptionType { get; set; }

            public string ExceptionMessage { get; set; }
        }

        public class RegisterArgs
        {
            public string AccountConnectionString { get; set; }

            public string ContainerName { get; set; }
        }

        public class RegisterArgsResponse
        {
            public string ResultUri { get; set; }
        }

        // Container is relative to accountConnectionString
        internal FuncSubmitModel RegisterFuncSubmitworker(object operation)
        {
            var services = GetServices();
#if false
            try
            {
                var userAccount = GetAccount(AccountName, AccountKey);
                CloudBlobClient client = userAccount.CreateCloudBlobClient();
                CloudBlobContainer container = client.GetContainerReference(ContainerName);
                container.FetchAttributes();
            }
            catch (Exception e)
            {
                ModelState.AddModelError(
            }
#endif
            var container = services.GetExecutionLogContainer();

            string blobResultName = string.Format(@"index\{0}.txt", Guid.NewGuid());
            var blobResults = container.GetBlobReference(blobResultName);

            string sasQueryString = blobResults.GetSharedAccessSignature(
                new SharedAccessPolicy
                {
                    Permissions = SharedAccessPermissions.Read | SharedAccessPermissions.Write,
                    SharedAccessExpiryTime = DateTime.UtcNow.AddMinutes(45)
                });

            IndexRequestPayload payload = new IndexRequestPayload
            {
                ServiceAccountConnectionString = services.AccountConnectionString,
                Writeback = blobResults.Uri.AbsoluteUri + sasQueryString,
                Operation = operation
            };

            IndexOperation indexOperation = operation as IndexOperation;
            FuncSubmitModel model = null;
            string msgStart = String.Empty;
            if (indexOperation != null)
            {
                string accountName = Utility.GetAccount(indexOperation.UserAccountConnectionString).Credentials.AccountName;
                msgStart = string.Format(@"Index request for {0}\{1} is in queue and waiting to be processed.", accountName, indexOperation.Blobpath);
                model = new RegisterFuncSubmitModel
                {
                    AccountName = accountName,
                    ContainerName = indexOperation.Blobpath,
                };
            }

            DeleteOperation deleteOperation = operation as DeleteOperation;
            if (deleteOperation != null)
            {
                msgStart = string.Format(@"Delete request for {0} is in queue and waiting to be processed.", deleteOperation.FunctionToDelete);
                model = new DeleteFuncSubmitModel
                {
                    FunctionToDelete = deleteOperation.FunctionToDelete,
                };
            }

            IndexUrlOperation urlOp = operation as IndexUrlOperation;
            if (urlOp != null)
            {
                msgStart = string.Format(@"Index request for {0} is in the queue and waiting to be processed.", urlOp.Url);
                model = new FuncSubmitModel();
            }

            if (model == null)
            {
                msgStart = "Unknown indexing operation";
            }

            // Get a shared access signature for the results blob
            string sas = blobResults.GetSharedAccessSignature(new SharedAccessPolicy()
            {
                Permissions = SharedAccessPermissions.Read,
                SharedAccessExpiryTime = DateTime.UtcNow.AddMinutes(45)
            });
            model.Writeback = new Uri(blobResults.Uri.AbsoluteUri + sas);
            

            // Upload some text as a placeholder while the message waits in the queue.
            // Exercise the SAS too before handing it back to the service.
            CloudBlob blob = new CloudBlob(payload.Writeback);
            blob.UploadText(msgStart);

            services.QueueIndexRequest(payload);

            return model;
        }
    }
}