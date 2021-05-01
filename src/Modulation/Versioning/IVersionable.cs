using System;
using Newtonsoft.Json;

namespace Nucs.JsonSettings.Modulation {
    public interface IVersionable {
        [JsonConverter(typeof(Newtonsoft.Json.Converters.VersionConverter))]
        public Version Version { get; set; }
    }
}