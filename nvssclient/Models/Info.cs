using System;
using System.Text.Json.Serialization;

namespace NVSSClient.Models
{
    public class Info : BaseEntity
    {
        public long Id { get; set; }
        public String Name {get; set;}
        public DateTime LastUpdated { get; set; }
    }
}