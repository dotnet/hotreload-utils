using System;
using System.Collections.Immutable;

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

    class DeltaRudeEditException : DiffyException {
        public DeltaRudeEditException () : base (exitStatus: 10) {}
        public DeltaRudeEditException( string message, ImmutableArray<EnC.RudeEditDiagnosticWrapper> rudeEdits) : base (message, exitStatus: 10) {
            _rudeEdits = rudeEdits;
        }

        public ImmutableArray<EnC.RudeEditDiagnosticWrapper> _rudeEdits;

        public override string Message {
            get {
                var rudes = new System.Text.StringBuilder("Rude edits:\n");
                foreach (var rude in _rudeEdits) {
                    rudes.AppendFormat("{0} at {1}\n", rude.KindWrapper, rude.Span);
                }
                return rudes.ToString() + base.Message;
            }

        }
    }
}
