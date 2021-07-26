//#define QUICKDEBUG

using System;
using System.Net;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

namespace Decomp2z64hdr
{
    class Program
    {
        static string z64hdrlink = "https://github.com/turpaan64/z64hdr/archive/refs/heads/main.zip";
        static string decomplink = "https://github.com/zeldaret/oot/archive/refs/heads/master.zip";
        static readonly string TempFolder = "temp";
        static readonly string z64HDRZip = $"{Program.TempFolder}/z64hdr.zip";
        static readonly string DecompZip = $"{Program.TempFolder}/decomp.zip";
        static readonly string zhdrmain = $"{Program.TempFolder}/z64hdr-main";
        static readonly string ootdebug = $"{Program.TempFolder}/oot-master";

        static void Main(string[] args)
        {
            if (args.Length > 0)
                Program.z64hdrlink = args[0];

            if (args.Length > 1)
                Program.decomplink = args[1];

#if !QUICKDEBUG
            if (Directory.Exists(Program.TempFolder))
                Directory.Delete(Program.TempFolder, true);

            Directory.CreateDirectory(Program.TempFolder);

            GetRepos(Program.z64hdrlink, Program.decomplink);
#endif

            string fnsymbols1_00 = $"{Program.zhdrmain}/oot_10_syms.ld";
            string fnsymbolsdecomp = $"{Program.zhdrmain}/oot_debug_syms.ld";

            Console.WriteLine("Getting old z64hdr symbols...");

            List<Symbol> sym1_00 = GetSymbolsFromLd(fnsymbols1_00);
            List<Symbol> symdec = GetSymbolsFromLd(fnsymbolsdecomp);

            Console.WriteLine("Copying the include folder from decomp...");

            string z64hdrincludedir = $"{Program.zhdrmain}/include";
            string decompincludedir = $"{Program.ootdebug}/include";

            Directory.Delete(z64hdrincludedir, true);
            CopyFilesRecursively(decompincludedir, z64hdrincludedir);

            Makez64HDRChanges(z64hdrincludedir);
        }

        private static void GetRepos(string linkhdr, string linkdecomp)
        {
            Console.WriteLine("Downloading z64hdr...");

            using (var client = new WebClient())
            {
                client.Headers.Add("user-agent", "Firefox");
                client.DownloadFile(linkhdr, Program.z64HDRZip);
            }

            Console.WriteLine("Downloading OoT Decomp...");

            using (var client = new WebClient())
            {
                client.Headers.Add("user-agent", "Firefox");
                client.DownloadFile(linkdecomp, Program.DecompZip);
            }

            if (Directory.Exists(Program.zhdrmain))
                Directory.Delete(Program.zhdrmain, true);

            if (Directory.Exists(Program.ootdebug))
                Directory.Delete(Program.ootdebug, true);

            Console.WriteLine("Unzipping z64hdr...");
            System.IO.Compression.ZipFile.ExtractToDirectory(Program.z64HDRZip, Program.TempFolder);

            Console.WriteLine("Unzipping OoT decomp...");
            System.IO.Compression.ZipFile.ExtractToDirectory(Program.DecompZip, Program.TempFolder);

            File.Delete(Program.DecompZip);
            File.Delete(Program.z64HDRZip);
        }

        private static List<Symbol> GetSymbolsFromLd(string filename)
        {
            string text = File.ReadAllText(filename);
            // Remove comments
            text = Regex.Replace(text, @"/\*(.|[\r\n])*?\*/", string.Empty);
            text = Regex.Replace(text, "//.+", string.Empty);

            string[] lines = text.Split('\n');
            List<Symbol> o = new();

            foreach (string ln in lines)
            {
                if (String.IsNullOrEmpty(ln))
                    continue;

                string[] split = ln.Split('=');

                if (split.Length != 2)
                    continue;

                o.Add(new Symbol(split[0].Trim(),
                                 Convert.ToUInt32(split[1].Replace("0x", "").Replace(";", "").Trim(), 16)));
            }

            return o;
        }

        private static void CopyFilesRecursively(string sourcePath, string targetPath)
        {
            //Now Create all of the directories
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }

            //Copy all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
            }
        }

        private static void Makez64HDRChanges(string includedir)
        {
            List<Z64HDRep> reps;

            try
            {
                string Json = File.ReadAllText("z64repls.json");
                reps = (List<Z64HDRep>)Newtonsoft.Json.JsonConvert.DeserializeObject(Json, typeof(List<Z64HDRep>));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not read or parse z64repls.json " + ex.Message);
                return;
            }

            foreach (Z64HDRep r in reps)
            {
                string fn = $"{includedir}/{r.filename}";
                string file = File.ReadAllText(fn);

                switch (r.type)
                {
                    case Z64HDRepT.typedefinsert:
                        {
                            string typedef = GetTypedefStruct(file, r.typedefname);
                            file = file.Replace(typedef, InsertLine(typedef, r.lineno, r.newtext));
                            break;
                        }
                    case Z64HDRepT.fileinsert:
                        {
                            file = InsertLine(file, r.lineno, r.newtext);
                            break;
                        }
                    case Z64HDRepT.textreplace:
                        {
                            file = file.Replace(r.replacedtext, r.newtext);
                            break;
                        }
                    case Z64HDRepT.exprreplace:
                        {
                            file = ReplaceExpr(file, r.replacedtext, r.newtext, RegexOptions.Multiline);
                            break;
                        }
                }

                File.WriteAllText(fn, file);
            }
        }

        private static string GetTypedefStruct(string filecontents, string structname)
        {
            Match r = Regex.Match(filecontents, @"typedef struct[\s\S]*?}\s*" + structname + ";");
            return r.Value[r.Value.LastIndexOf("typedef struct")..];
        }

        private static string InsertLine(string text, int lineindex, string toinsert)
        {
            List<string> lines = Regex.Split(text, @"(?<=[\n])").ToList();
            lines.Insert(lineindex, toinsert + Environment.NewLine);

            return string.Join("", lines.ToArray());
        }

        static public string ReplaceExpr(string Orig, string Expr, string Replacement, RegexOptions regexOptions = RegexOptions.None)
        {
            string Pattern = String.Format(@"\b{0}\b", Regex.Escape(Expr));
            return Regex.Replace(Orig, Pattern, Replacement, regexOptions);
        }
    }

    public enum Z64HDRepT
    {
        typedefinsert,
        fileinsert,
        textreplace,
        exprreplace,
    }

    public class Z64HDRep
    {
        public Z64HDRepT type;
        public string filename;
        public string replacedtext;
        public string typedefname;
        public string newtext;
        public int lineno;

        public Z64HDRep()
        { }
    }

    public class Symbol
    {
        public UInt32 Addr;
        public string Name;

        public Symbol(string _Name, UInt32 _Addr)
        {
            Addr = _Addr;
            Name = _Name;
        }
    }
}
