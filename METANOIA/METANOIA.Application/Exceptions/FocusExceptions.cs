using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Application.Exceptions
{
    public sealed class FocusNotFoundException(string message)
    : Exception(message);

    public sealed class FocusConflictException(
        string message,
        object? details = null) : Exception(message)
    {
        public object? Details { get; } = details;
    }

    public sealed class FocusForbiddenException(string message)
        : Exception(message);

    public sealed class FocusValidationException(string message)
        : Exception(message);
}
