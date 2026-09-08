namespace HighSchoolEcosystem.Models;

/// <summary>
/// An assignment together with the class it was set for. Assignments live in a
/// subcollection under their class, so the class name isn't on the assignment
/// document — but a student looking at "everything due" needs to see it.
/// </summary>
public sealed record ClassAssignment(string ClassId, string ClassName, Assignment Assignment);
