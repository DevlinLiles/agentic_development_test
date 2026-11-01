// Removed: this class carried a duplicate `AddChessInfrastructure` extension method that
// collided (CS0121 ambiguous call) with the real composition root in DependencyInjection.cs
// for every caller. The only live call site (ChessMvp.Api/Program.cs) already resolves
// DependencyInjection.AddChessInfrastructure, so the file is deleted rather than kept as an
// alias. Kept as an empty placeholder via this explanatory comment; no code is defined here.
