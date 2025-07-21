// See https://aka.ms/new-console-template for more information
using Microsoft.KernelMemory;

Console.WriteLine("Hello, World!");

var memory = new MemoryWebClient("https://localhost:7181", "FfxFT4Twbyv3RBocPY00gXFnaIDt3z3PsYiiChH4eEqhlfsdfsdfsdHv6XJ3w3AAABACOGIgye"); // <== URL of KM web service

// Import a file
await memory.ImportDocumentAsync("C:\\DATA\\test.docx");

var answer = await memory.SearchAsync("How I can claim for the POV");

Console.WriteLine(answer.Query);

foreach (var x in answer.Results)
{
    Console.WriteLine($"  * {x.SourceName} -- {x.Partitions.First().Text}");
}

