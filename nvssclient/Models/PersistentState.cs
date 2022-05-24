using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NVSSClient.Models
{
    public class PersistentState : BaseEntity
    {
        public long Id { get; set; }
        public DateTime LastUpdated { get; set; }

    }
}