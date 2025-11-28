using System;
using System.Collections.Generic;
using System.Text;

namespace EfCore.FastExtensions.Tests.Models
{
    internal class State
    {
        public int Id { get; set; }
        public string StateCode { get; set; } = "";
        public string StateName { get; set; } = "";
        public Country? Country { get; set; }
        public int CountryId { get; set; }
    }
}
