namespace ChessMvp.Infrastructure;

// Historical note: this file once duplicated AddChessInfrastructure (a merge leftover), which
// made the extension ambiguous at every call site (CS0121). The single, canonical definition
// lives in ServiceCollectionExtensions.cs; this file intentionally declares nothing so the
// Infrastructure assembly compiles and Program.cs's AddChessInfrastructure call resolves to
// exactly one method.
