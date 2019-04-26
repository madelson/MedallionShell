using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SampleCommand
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            Log("started: " + string.Join(", ", args.Select(a => "'" + a + "'")));

            string line;
            switch (args[0])
            {
                case "echo":
                    var isPerChar = args.Contains("--per-char");
                    var encoding = args.Contains("--utf8") ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                        : args.Contains("--utf162") ? new UnicodeEncoding(bigEndian: false, byteOrderMark: false)
                        : default(Encoding);
                    if (encoding != null)
                    {
                        Console.InputEncoding = Console.OutputEncoding = encoding;
                    }

                    if (isPerChar)
                    {
                        int ch;
                        while ((ch = Console.In.Read()) != -1)
                        {
                            Console.Out.Write((char)ch);
                            Console.Out.Flush();
                        }
                    }
                    else
                    {
                        var input = Console.In.ReadToEnd();
                        Console.Out.Write(input);
                    }
                    break;
                case "errecho":
                    var errEchoInput = Console.In.ReadToEnd();
                    (args[0].StartsWith("err") ? Console.Error : Console.Out).Write(errEchoInput);
                    break;
                case "grep":
                    var regex = new Regex(args[1]);
                    while ((line = Console.ReadLine()) != null)
                    {
                        //Log("Read '{0}'", line);
                        if (regex.Match(line).Success)
                        {
                            Console.WriteLine(line);
                            //Log("Wrote '{0}'", line);
                        }
                    }
                    break;
                case "head":
                    var count = int.Parse(args[1]);
                    var i = 0;
                    while ((i++) < count && (line = Console.ReadLine()) != null)
                    {
                        Console.WriteLine(line);
                    }
                    break;
                case "exit":
                    var code = int.Parse(args[1]);
                    Environment.Exit(code);
                    break;
                case "argecho":
                    foreach (var argument in args.Skip(1))
                    {
                        Console.WriteLine(argument);
                    }
                    break;
                case "sleep":
                    Log("Sleeping for " + args[1]);
                    Thread.Sleep(int.Parse(args[1]));
                    break;
                case "bool":
                    Console.WriteLine(args[2]);
                    Console.Out.Flush();
                    if (!bool.Parse(args[1]))
                    {
                        Environment.Exit(1);
                    }
                    break;
                case "pipe":
                    string pipeLine;
                    while ((pipeLine = Console.In.ReadLine()) != null)
                    {
                        Console.Out.WriteLine(pipeLine);
                        Console.Out.Flush();
                    }
                    break;
                case "pipebytes":
                    using (var standardInput = Console.OpenStandardInput())
                    using (var standardOutput = Console.OpenStandardOutput())
                    {
                        var buffer = new byte[10];
                        while (true)
                        {
                            var bytesRead = standardInput.Read(buffer, 0, buffer.Length);
                            if (bytesRead == 0) { break; }
                            standardOutput.Write(buffer, 0, bytesRead);
                            standardOutput.Flush();
                        }
                    }
                    break;
                case "shortflush":
                    Console.Out.Write(args[1]);
                    Console.Out.Flush();
                    // don't exit until stdin closes
                    while (Console.ReadLine() != null)
                    {
                        Thread.Sleep(5);
                    }
                    break;
                case "echoLinesToBothStreams":
                    async Task EchoLinesAsync(TextWriter output)
                    {
                        while (true)
                        {
                            string lineToEcho;
                            lock (Console.In)
                            {
                                lineToEcho = Console.In.ReadLine(); // no async due to lock
                            }
                            if (lineToEcho == null) { return; }

                            await output.WriteLineAsync(lineToEcho);
                        }
                    }
                    Task.WaitAll(Task.Run(() => EchoLinesAsync(Console.Error)), Task.Run(() => EchoLinesAsync(Console.Out)));
                    break;
                case nameof(PlatformCompatibilityTests):
                    var method = typeof(PlatformCompatibilityTests).GetMethod(args[1]);
                    if (method == null)
                    {
                        throw new ArgumentException($"Unknown test method '{args[1]}'");
                    }
                    method.Invoke(null, new object[0]);
                    break;
                default:
                    Console.Error.WriteLine("Unrecognized mode " + args[0]);
                    Environment.Exit(-1);
                    break;
            }

            Log("Exited normally");
        }

        [Conditional("TESTING")]
        public static void Log(string format, params object[] args)
        {
            var baseText = string.Format("{0:h:m:ss.fff} {1} ({2}): ", DateTime.Now, Process.GetCurrentProcess().Id, string.Join(" ", Environment.GetCommandLineArgs()));
            var text = baseText + string.Format(format, args);

            using (var fs = new FileStream("log.txt", FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(fs))
            {
                writer.WriteLine(text);
            }
        }
    }
}
