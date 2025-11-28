using System;
using System.Collections.Generic;
using System.Text;

namespace EfCore.FastExtensions.Tests.Models
{
    internal class Country
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public ICollection<State>? States { get; set; }
    }
}
