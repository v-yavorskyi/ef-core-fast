using System;
using System.Collections.Generic;
using System.Text;

namespace EfCore.FastExtensions.Tests.Models
{
    internal class User
    {
        public int Id { get; set; }
        public int StateId { get; set; }
        public string Region { get; set; }
        public State? State { get; set; }
    }
}
