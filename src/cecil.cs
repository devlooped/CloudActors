#:package Mono.Cecil@0.11.*
#:property Nullable=enable
#:property ManagePackageVersionsCentrally=false
#:property PublishAot=false
#:property IsPackable=false

using System;
using System.IO;
using Mono.Cecil;

if (args.Length < 2)
{
    Console.WriteLine("Usage: cecil <assemblyPath> <typeFullName>");
    return -1;
}

var (assemblyPath, typeName) = (args[0], args[1]);
using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

var typeToRemove = assembly.MainModule.GetType(typeName);
if (typeToRemove != null)
{
    assembly.MainModule.Types.Remove(typeToRemove);
    string tempPath = Path.GetTempFileName();
    assembly.Write(tempPath);
    assembly.Dispose();
    File.Move(tempPath, assemblyPath, true);
    Console.WriteLine($"Removed type '{typeName}' from {assemblyPath}.");
}
else
{
    Console.WriteLine($"Type '{typeName}' not found.");
}

return 0;