﻿using System;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Wire protocol for an serializing triggers.
    /// Irrelevant fields should reamin null.
    /// </summary>
    internal class TriggerRaw
    {
        /// <summary>
        /// Define what type of trigger. 
        /// Serializing can emit as either the raw number or the name.
        /// </summary>
        public TriggerType Type { get; set; }

        /// <summary>
        /// For Blobs, blob path for the input. This is of the form "container/blob"
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string BlobInput { get; set; }

        /// <summary>
        /// For Blobs, optional. semicolon delimited list of blob paths for the output. This is of the form 
        /// "container1/blob1;container2/blob2"
        /// The trigger is not fired if all outputs have a more recent modified timestamp than the input. 
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string BlobOutput { get; set; }

        /// <summary>
        /// For Queues. The name of the azure queue. Be sure to follow azure queue naming rules, including all lowercase.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string QueueName { get; set; }

        /// <summary>
        /// For Service Bus. The name of the entity (queue or topic/subscription).
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string EntityName { get; set; }

        /// <summary>
        /// Create a new trigger on blobs. This fires the callback if a new input blob is detected. The http content is the string name of the blob path that was detected. 
        /// For example, if input is 'container/{name}.txt', and output is 'container/outputs/{nane}.txt;
        /// </summary>
        /// <param name="blobInput">An input path to search for. The blob name can include 'route parameters' for pattern matching, is and of the form 'container/blob'. </param>
        /// <param name="blobOutput">A semicolon delimited list of output paths. The trigger is not fired if all outputs are newer than the input. 
        /// This can have route parameters which are populated from the capture at the input values.</param>
        /// <returns>A trigger object.</returns>
        public static TriggerRaw NewBlob(string blobInput, string blobOutput = null)
        {
            return new TriggerRaw
            {
                Type = TriggerType.Blob,
                BlobInput = blobInput,
                BlobOutput = blobOutput
            };
        }

        /// <summary>
        /// Create a new trigger on queue message. This fires the callback when a new queue message is detected, where the http request contents are the azure queue message contents. 
        /// The azure message is deleted if the callback is invoked. 
        /// </summary>
        /// <param name="queueName">The azure queue to listen on. Be sure to adhere to azure queue naming rules, including being all lowercase.</param>
        /// <returns>A trigger object.</returns>
        public static TriggerRaw NewQueue(string queueName)
        {
            return new TriggerRaw
            {
                Type = TriggerType.Queue,
                QueueName = queueName
            };
        }

        public static TriggerRaw NewServiceBus(string entityName)
        {
            return new TriggerRaw
            {
                Type = TriggerType.ServiceBus,
                EntityName = entityName
            };
        }
    }
}