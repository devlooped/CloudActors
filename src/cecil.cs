#:package Mono.Cecil@0.11.*
#:property Nullable=enable
#:property ManagePackageVersionsCentrally=false
#:property PublishAot=false
#:property IsPackable=false

using System.Linq;
using Mono.Cecil;

if (args.Length < 2)
{
    Console.WriteLine("Usage: cecil <assemblyPath> <typeFullName>");
    Console.WriteLine("       cecil <assemblyPath> --strip-attribute <typeFullName> <attributeName>");
    return -1;
}

var assemblyPath = args[0];
using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadWrite = true });

if (args.Length >= 4 && args[1] == "--strip-attribute")
{
    var typeName = args[2];
    var attrName = args[3];
    var type = assembly.MainModule.GetType(typeName);
    if (type == null)
    {
        Console.WriteLine($"Type '{typeName}' not found in '{assemblyPath}'.");
        return 1;
    }

    var attr = type.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == attrName || a.AttributeType.FullName == attrName);
    if (attr != null)
    {
        type.CustomAttributes.Remove(attr);
        string tempPath = Path.GetTempFileName();
        assembly.Write(tempPath);
        assembly.Dispose();
        File.Move(tempPath, assemblyPath, true);
        Console.WriteLine($"Removed [{attrName}] from '{typeName}' in {assemblyPath}.");
    }
    else
    {
        Console.WriteLine($"Attribute '{attrName}' not found on type '{typeName}'.");
    }
}
else
{
    var typeName = args[1];
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
        Console.WriteLine($"Type '{typeName}' not found in '{assemblyPath}'.");
    }
}

return 0;