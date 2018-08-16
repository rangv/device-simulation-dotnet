// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.Services.Diagnostics;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.Services.IotHub;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.Services.Models;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.WebService.v1.Exceptions;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.WebService.v1.Models.Helpers;
using Newtonsoft.Json;

namespace Microsoft.Azure.IoTSolutions.DeviceSimulation.WebService.v1.Models.SimulationApiModel
{
    public class SimulationApiModel
    {
        private const string DATE_FORMAT = "yyyy-MM-dd'T'HH:mm:sszzz";

        private DateTimeOffset created;
        private DateTimeOffset modified;

        [JsonProperty(PropertyName = "ETag")]
        public string ETag { get; set; }

        [JsonProperty(PropertyName = "Id")]
        public string Id { get; set; }

        [JsonProperty(PropertyName = "Name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "Desc", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty(PropertyName = "Enabled")]
        public bool? Enabled { get; set; }

        [JsonProperty(PropertyName = "IoTHub")]
        public SimulationIotHub IotHub { get; set; }

        [JsonProperty(PropertyName = "StartTime", NullValueHandling = NullValueHandling.Ignore)]
        public string StartTime { get; set; }

        [JsonProperty(PropertyName = "EndTime", NullValueHandling = NullValueHandling.Ignore)]
        public string EndTime { get; set; }

        [JsonProperty(PropertyName = "StopTime", NullValueHandling = NullValueHandling.Ignore)]
        public string StopTime { get; set; }

        [JsonProperty(PropertyName = "DeviceModels")]
        public IList<SimulationDeviceModelRef> DeviceModels { get; set; }

        [JsonProperty(PropertyName = "TotalMsgs", NullValueHandling = NullValueHandling.Ignore)]
        public int TotalMessagesSent { get; set; }

        [JsonProperty(PropertyName = "AvgMsgs", NullValueHandling = NullValueHandling.Ignore)]
        public double AverageMessagesSent { get; set; }

        [JsonProperty(PropertyName = "$metadata", Order = 1000)]
        public IDictionary<string, string> Metadata => new Dictionary<string, string>
        {
            { "$type", "Simulation" },
            { "$uri", "/" + Version.PATH + "/simulations/" + this.Id },
            { "$created", this.created.ToString(DATE_FORMAT) },
            { "$modified", this.modified.ToString(DATE_FORMAT) }
        };

        // Default constructor used by web service requests
        public SimulationApiModel()
        {
            this.Id = string.Empty;
            this.Name = string.Empty;

            // When unspecified, a simulation is enabled
            this.Enabled = true;
            this.IotHub = null;
            this.StartTime = null;
            this.EndTime = null;
            this.StopTime = null;
            this.DeviceModels = new List<SimulationDeviceModelRef>();
            this.TotalMessagesSent = 0;
            this.AverageMessagesSent = 0;
        }

        // Map API model to service model
        public Simulation ToServiceModel(string id = "")
        {
            this.Id = id;

            var now = DateTimeOffset.UtcNow;

            var result = new Simulation
            {
                ETag = this.ETag,
                Id = this.Id,
                Name = this.Name,
                Description = this.Description,
                // When unspecified, a simulation is enabled
                Enabled = this.Enabled ?? true,
                StartTime = DateHelper.ParseDateExpression(this.StartTime, now),
                EndTime = DateHelper.ParseDateExpression(this.EndTime, now),
                StopTime = DateHelper.ParseDateExpression(this.StopTime, now),
                IotHubConnectionString = SimulationIotHub.ToServiceModel(this.IotHub),
                DeviceModels = this.DeviceModels?.Select(x => x.ToServiceModel()).ToList(),
                TotalMessagesSent = this.TotalMessagesSent,
                AverageMessagesSent = this.AverageMessagesSent
            };

            return result;
        }

        // Map service model to API model
        public static SimulationApiModel FromServiceModel(Simulation value)
        {
            if (value == null) return null;

            var result = new SimulationApiModel
            {
                ETag = value.ETag,
                Id = value.Id,
                Name = value.Name,
                Description = value.Description,
                Enabled = value.Enabled,
                StartTime = value.StartTime.ToString(),
                EndTime = value.EndTime.ToString(),
                StopTime = value.StopTime.ToString(),
                IotHub = new SimulationIotHub(value.IotHubConnectionString),
                TotalMessagesSent = value.TotalMessagesSent,
                AverageMessagesSent = value.AverageMessagesSent
            };

            // Ignore the date if the simulation doesn't have a start time
            if (value.StartTime.HasValue && !value.StartTime.Value.Equals(DateTimeOffset.MinValue))
            {
                result.StartTime = value.StartTime?.ToString(DATE_FORMAT);
            }

            // Ignore the date if the simulation doesn't have an end time
            if (value.EndTime.HasValue && !value.EndTime.Value.Equals(DateTimeOffset.MaxValue))
            {
                result.EndTime = value.EndTime?.ToString(DATE_FORMAT);
            }

            // Ignore the date if the simulation doesn't have an end time
            if (value.StopTime.HasValue && !value.StopTime.Value.Equals(DateTimeOffset.MaxValue))
            {
                result.StopTime = value.StopTime?.ToString(DATE_FORMAT);
            }

            result.DeviceModels = SimulationDeviceModelRef.FromServiceModel(value.DeviceModels);
            result.created = value.Created;
            result.modified = value.Modified;

            return result;
        }

        public async Task ValidateInputRequest(ILogger log, IIotHubConnectionStringManager connectionStringManager)
        {
            const string NO_DEVICE_MODEL = "The simulation doesn't contain any device model";
            const string ZERO_DEVICES = "The simulation has zero devices";
            const string END_TIME_BEFORE_START_TIME = "The simulation End Time must be after the Start Time";
            const string INVALID_DATE = "Invalid date format";
            const string CANNOT_RUN_IN_THE_PAST = "The simulation end date is in the past";

            // A simulation must contain at least one device model
            if (this.DeviceModels.Count < 1)
            {
                log.Error(NO_DEVICE_MODEL, () => new { simulation = this });
                throw new BadRequestException(NO_DEVICE_MODEL);
            }

            // A simulation must use at least one device
            if (this.DeviceModels.Sum(x => x.Count) < 1)
            {
                log.Error(ZERO_DEVICES, () => new { simulation = this });
                throw new BadRequestException(ZERO_DEVICES);
            }

            try
            {
                var now = DateTimeOffset.UtcNow;
                var startTime = DateHelper.ParseDateExpression(this.StartTime, now);
                var endTime = DateHelper.ParseDateExpression(this.EndTime, now);
                // The start time must be before the end time
                if (startTime.HasValue && endTime.HasValue && startTime.Value.Ticks >= endTime.Value.Ticks)
                {
                    log.Error(END_TIME_BEFORE_START_TIME, () => new { simulation = this });
                    throw new BadRequestException(END_TIME_BEFORE_START_TIME);
                }

                // The end time cannot be in the past
                if (endTime.HasValue && endTime.Value.Ticks <= now.Ticks)
                {
                    log.Error(CANNOT_RUN_IN_THE_PAST, () => new { simulation = this });
                    throw new BadRequestException(CANNOT_RUN_IN_THE_PAST);
                }
            }
            catch (InvalidDateFormatException e)
            {
                log.Error(INVALID_DATE, () => new { simulation = this });
                throw new BadRequestException(INVALID_DATE, e);
            }

            await connectionStringManager.ValidateConnectionStringAsync(this.IotHub.ConnectionString);
        }
    }
}
