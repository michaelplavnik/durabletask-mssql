﻿namespace DurableTask.SqlServer.Tests.Utils
{
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using DurableTask.Core;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Xunit;

    class TestInstance<T>
    {
        readonly TaskHubClient client;
        readonly OrchestrationInstance instance;
        readonly DateTime startTime;
        readonly T input;

        public TestInstance(
            TaskHubClient client,
            OrchestrationInstance instance,
            DateTime startTime,
            T input)
        {
            this.client = client;
            this.instance = instance;
            this.startTime = startTime;
            this.input = input;
        }

        OrchestrationInstance GetInstanceForAnyExecution() => new OrchestrationInstance
        {
            InstanceId = this.instance.InstanceId,
        };

        public async Task<OrchestrationState> WaitForStart(TimeSpan timeout = default)
        {
            AdjustTimeout(ref timeout);

            Stopwatch sw = Stopwatch.StartNew();
            do
            {
                OrchestrationState state = await this.GetStateAsync();
                if (state != null && state.OrchestrationStatus != OrchestrationStatus.Pending)
                {
                    return state;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500));

            } while (sw.Elapsed < timeout);

            throw new TimeoutException($"Orchestration with instance ID '{this.instance.InstanceId}' failed to start.");
        }

        public async Task<OrchestrationState> WaitForCompletion(
            TimeSpan timeout = default,
            OrchestrationStatus expectedStatus = OrchestrationStatus.Completed,
            object expectedOutput = null,
            string expectedOutputRegex = null,
            bool continuedAsNew = false)
        {
            AdjustTimeout(ref timeout);

            OrchestrationState state = await this.client.WaitForOrchestrationAsync(this.GetInstanceForAnyExecution(), timeout);
            Assert.NotNull(state);
            Assert.Equal(expectedStatus, state.OrchestrationStatus);

            if (this.input != null)
            {
                Assert.Equal(JToken.FromObject(this.input), JToken.Parse(state.Input));
            }
            else
            {
                Assert.Null(state.Input);
            }

            // For created time, account for potential clock skew
            Assert.True(state.CreatedTime >= this.startTime.AddMinutes(-5));
            Assert.True(state.LastUpdatedTime > state.CreatedTime);
            Assert.True(state.CompletedTime > state.CreatedTime);
            Assert.NotNull(state.OrchestrationInstance);
            Assert.Equal(this.instance.InstanceId, state.OrchestrationInstance.InstanceId);

            if (continuedAsNew)
            {
                Assert.NotEqual(this.instance.ExecutionId, state.OrchestrationInstance.ExecutionId);
            }
            else
            {
                Assert.Equal(this.instance.ExecutionId, state.OrchestrationInstance.ExecutionId);
            }

            if (expectedOutput != null)
            {
                Assert.NotNull(state.Output);
                try
                {
                    // DTFx usually encodes outputs as JSON values. The exception is error messages.
                    // If this is an error message, we'll throw here and try the logic in the catch block.
                    JToken.Parse(state.Output);
                    Assert.Equal(JToken.FromObject(expectedOutput).ToString(Formatting.None), state.Output);
                }
                catch (JsonReaderException)
                {
                    Assert.Equal(expectedOutput, state?.Output);
                }
            }

            if (expectedOutputRegex != null)
            {
                Assert.Matches(expectedOutputRegex, state.Output);
            }

            return state;
        }

        internal Task<OrchestrationState> GetStateAsync()
        {
            return this.client.GetOrchestrationStateAsync(this.instance);
        }

        internal Task RaiseEventAsync(string name, object value)
        {
            return this.client.RaiseEventAsync(this.instance, name, value);
        }

        internal Task TerminateAsync(string reason)
        {
            return this.client.TerminateInstanceAsync(this.instance, reason);
        }

        static void AdjustTimeout(ref TimeSpan timeout)
        {
            if (timeout == default)
            {
                timeout = TimeSpan.FromSeconds(10);
            }

            if (Debugger.IsAttached)
            {
                TimeSpan debuggingTimeout = TimeSpan.FromMinutes(5);
                if (debuggingTimeout > timeout)
                {
                    timeout = debuggingTimeout;
                }
            }
        }
    }
}
