using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Akka.Streams.Azure.StorageQueue.Tests
{
    /// <summary>
    /// Load "environment.json" file as default environment variables.
    /// Environment variables are only set if they are not found, and removed on dispose.
    /// </summary>
    public sealed class EnvironmentVariableFixture : IDisposable
    {
        private readonly List<string> _variables = new List<string>();

        public EnvironmentVariableFixture()
        {
            if (!File.Exists("environment.json"))
                return;

            using (var file = File.OpenText("environment.json"))
            {
                var reader = new JsonTextReader(file);
                var jObject = JObject.Load(reader);

                var variables = jObject.Children<JProperty>();
                foreach (var variable in variables)
                {
                    var value = Environment.GetEnvironmentVariable(variable.Name);
                    if (value != null) continue;

                    _variables.Add(variable.Name);
                    Environment.SetEnvironmentVariable(variable.Name, variable.Value.ToString());
                }
            }
        }

        public void Dispose()
        {
            foreach (var variable in _variables)
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
        }
    }
}