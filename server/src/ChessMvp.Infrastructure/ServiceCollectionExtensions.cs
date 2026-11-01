// Deleted in this QA change: this file declared a duplicate `AddChessInfrastructure`
// extension method (identical namespace and signature) alongside the newer
// DependencyInjection.cs, which made Program.cs's call ambiguous (CS0121) — the whole
// ChessMvp.Api project failed to compile. Its registrations (DbContext with the
// "ChessMvpDb" connection string, the game repository and the rules-engine adapter) are
// fully subsumed by DependencyInjection.cs, so nothing is lost by removing the duplicate.
// The file is left comment-only (deleting it outright is not possible with the available
// tooling); a comment-only .cs file compiles to nothing and declares no types.
