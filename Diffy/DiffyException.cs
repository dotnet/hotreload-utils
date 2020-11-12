using System;

namespace Diffy
{
    public class DiffyException : Exception {
        public int ExitStatus { get; }

        public DiffyException(int exitStatus) : base () {
            ExitStatus = exitStatus;
        }

        public DiffyException (string message, int exitStatus) : base (message) {
            ExitStatus = exitStatus;
        }

        public DiffyException (string message, Exception innerException, int exitStatus) : base (message, innerException) {
            ExitStatus = exitStatus;
        }
    }

    class DeltaCompilationException : DiffyException {
        public DeltaCompilationException(int exitStatus = 1) : base (exitStatus) {}

        public DeltaCompilationException(string message, int exitStatus = 1) : base (message, exitStatus) {}
        public DeltaCompilationException(string message, Exception innerException, int exitStatus = 1) : base (message, innerException, exitStatus) {}
    }

    class AdhocBaselineException : DiffyException {
        public AdhocBaselineException(int exitStatus = 1) : base (exitStatus) {}

        public AdhocBaselineException(string message, int exitStatus = 1) : base (message, exitStatus) {}
        public AdhocBaselineException(string message, Exception innerException, int exitStatus = 1) : base (message, innerException, exitStatus) {}
    }
}
