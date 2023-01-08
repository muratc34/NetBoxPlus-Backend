using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Contracts
{
    public class GenreCreatedEvent
    {
        public Guid Id { get; set; }
        public int GenreCode { get; set; }
        public string? Title { get; set; }
    }
}
