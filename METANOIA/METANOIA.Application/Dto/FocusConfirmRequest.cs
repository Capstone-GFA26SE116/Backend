using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Application.Dto
{
    public sealed record FocusConfirmRequest(
    bool Confirmed,
    int? CorrectedMinutes
);
}
