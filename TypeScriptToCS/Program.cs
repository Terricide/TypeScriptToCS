﻿using Microsoft.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TypeScriptToCS
{
    static class Program
    {
        static string GetTypeWithOutGenerics (string value)
        {
            var index = value.IndexOf('<');
            if (index == -1)
                return value;
            else
                return value.Substring(0, index);
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Typescript file location?");
            string tsFileLocation = Console.ReadLine();
            string tsFile = "";

            try
            {
                tsFile = File.ReadAllText(tsFileLocation);
            }
            catch (Exception e)
            {
                Console.WriteLine(e + " occured when trying to read file. Press enter to exit. Press t to rethrow.");
                ConsoleKey key;
                while ((key = Console.ReadKey(true).Key) != ConsoleKey.Enter)
                    if (key == ConsoleKey.R)
                        throw;
                Environment.Exit(0);
            }

            int index = 0;
            ReadTypeScriptFile(tsFile, ref index, nameSpaceDefinitions);

            string endFile = "using System;\nusing Bridge;\nusing Bridge.Html5;\nusing Bridge.WebGL;\nusing any = System.Object;\nusing boolean = System.Boolean;\nusing Function = System.Delegate;\nusing RegExp = Bridge.Text.RegularExpressions.Regex;\nusing number = System.Double;\nusing Number = System.Double;\n\n";

            foreach (var namespaceItem in nameSpaceDefinitions)
            {
                if (string.IsNullOrEmpty(namespaceItem.name))
                    continue;
                if (namespaceItem.typeDefinitions.Count == 1)
                {
                    if (namespaceItem.typeDefinitions[0].name == "GlobalClass")
                    {
                        namespaceItem.typeDefinitions[0].name = namespaceItem.name;
                        (namespaceItem.typeDefinitions[0] as ClassDefinition).@static = true;
                        ProcessTypeDefinition(namespaceItem.typeDefinitions[0], ref endFile, namespaceItem);
                        continue;
                    }
                }
                if ((namespaceItem.name ?? "") != "")
                    endFile += $"namespace {namespaceItem.name}\n{ "{" }\n";

                List<string> names = new List<string>();
                foreach (var rItem in namespaceItem.typeDefinitions)
                {
                    if (!names.Contains(rItem.name))
                    {
                        names.Add(rItem.name);
                        ProcessTypeDefinition(rItem, ref endFile, namespaceItem);
                    }
                }

                if ((namespaceItem.name ?? "") != "")
                    endFile += "\n}\n";
            }

            Console.WriteLine("Choose a file name...");
            File.WriteAllText(Console.ReadLine(), endFile);
        }

        static List<NamespaceDefinition> nameSpaceDefinitions = new List<NamespaceDefinition>();

        public static void ProcessTypeDefinition (TypeDefinition rItem, ref string endFile, NamespaceDefinition @namespace)
        {
            if (rItem is ClassDefinition)
            {
                ClassDefinition classItem = (ClassDefinition)rItem;
                if (classItem.name == "GlobalClass" && classItem.fields.Count == 0 && classItem.methods.Count == 0/* && classItem.properties.Count == 0*/)
                    return;
                string extendString = classItem.extends.Count != 0 ? " : " : string.Empty;

                List<Field> fields;
                List<Method> methods;
                if (classItem.type == TypeType.@class && !classItem.extends.All(v =>
                {
                    var type = FindType(v) as ClassDefinition;
                    if (type == null)
                        return false;
                    else
                        return type.type == TypeType.@class;
                }))
                    GetMethodsAndFields(classItem, out fields, out methods);
                else
                {
                    fields = new List<Field>(classItem.fields);
                    methods = new List<Method>(classItem.methods);
                }

                if (classItem.type == TypeType.@interface && !(classItem.fields.Count == 0 && classItem.methods.Count == 0/* && classItem.properties.Count == 0*/))
                {
                    List<Field> mFields;
                    List<Method> mMethods;
                    GetMethodsAndFields(classItem, out mFields, out mMethods);
                    endFile += $"\t[ObjectLiteral]\n\tpublic class JSON{classItem.name} : {classItem.name}\n\t{"{"}";
                    List<string> fieldNames = new List<string>();
                    List<Method> methodsDone = new List<Method>();
                    foreach (var item in mFields)
                        if (!fieldNames.Contains(item.typeAndName.name))
                        {
                            fieldNames.Add(item.typeAndName.name);
                            endFile += $"\n#pragma warning disable CS0626\n\t\tpublic extern {item.typeAndName.type.Replace("$", "DollarSign")}{item.typeAndName.OptionalString} {item.CSName}" + " { get; set; }\n#pragma warning restore CS0626";
                        }
                    foreach (var item in mMethods)
                    {
                        if (methodsDone.Any(v => item.typeAndName.name == v.typeAndName.name))
                            continue;
                        var itemClone = item.Clone();
                        itemClone.typeAndName = itemClone.typeAndName.Clone();
                        if (itemClone.typeAndName.name == "")
							itemClone.typeAndName.name = FormatName(classItem.name, "IndexerDelegate");
                        else
							itemClone.typeAndName.name = FormatName(itemClone.typeAndName.name, "Delegate");
                        endFile += "\n";
                        ProcessTypeDefinition(itemClone, ref endFile, @namespace);
                        endFile += $"\n#pragma warning disable CS0626\n\t\tpublic extern {item.typeAndName.type.Replace("$", "DollarSign")} " + (item.indexer ? "this" : item.CSName) + $" {item.StartBracket}" + string.Join(", ", item.parameters.ConvertAll(v => (v.@params ? "params " : string.Empty) + v.type + " " + ChangeName(v.name) + (v.optional ? " = default(" + v.type + ")" : ""))) + item.EndBracket + (item.indexer ? " { get; set; }" : ";") + "\n#pragma warning restore CS0626";
                        endFile += "\n#pragma warning disable CS0626\n\t\tpublic extern ";
                        endFile += itemClone.name;
                        endFile += " ";
                        if (string.IsNullOrEmpty(item.typeAndName.name))
                            endFile += "indexer";
                        else
                            endFile += ChangeName(char.ToLower(item.CSName[0]) + item.CSName.Substring(1));
                        endFile += " { get; set; }\n#pragma warning restore CS0626";
                        methodsDone.Add(item);
                    }
                    endFile += "\n\t}\n";
                }

                string abstractString = classItem.@abstract ? "abstract " : string.Empty;
                string staticString = classItem.@static ? "static " : string.Empty;

                endFile += "\t[External]" + (classItem.name == "GlobalClass" ? $"\n\t[Name(\"{@namespace.name}\")]" : "") + $"\n\tpublic {staticString}{abstractString}{classItem.type} {ChangeName(classItem.name)}{extendString}{string.Join(", ", classItem.extends.ConvertAll(GetType)) + GetWhereString(classItem.typeWheres) + "\n\t{"}";

                string interfacePublic = classItem.type != TypeType.@interface ? "extern " : string.Empty;
                string pragmaStart = (classItem.type == TypeType.@class ? "\n#pragma warning disable CS0626" : string.Empty);
                string pragmaEnd = (classItem.type == TypeType.@class ? "\n#pragma warning restore CS0626" : string.Empty);
                
                foreach (var item in fields)
                    endFile += pragmaStart + "\n\t\t[FieldProperty]\n\t\t" + (char.IsUpper(item.typeAndName.name[0]) ? "[Name(false)]\n\t\t" : "") + (classItem.type != TypeType.@interface ? "public " : string.Empty) + $"{interfacePublic}" + (item.@static || classItem.name == "GlobalClass" ? "static " : "") + $"{item.typeAndName.type}{item.typeAndName.OptionalString} {item.CSName}" + " { get; set; }" + pragmaEnd;

                List<Method> methoders = new List<Method>();
                foreach (var item in methods)
                    if (!methoders.Any(v => v.typeAndName.name == item.typeAndName.name && v.parameters.SequenceEqual(item.parameters) && v.typeAndName.type == item.typeAndName.type))
                    {
                        methoders.Add(item);
                        if (item.typeAndName.name == "constructor")
                            endFile += $"\n#pragma warning disable CS0824\n\t\tpublic extern {GetTypeWithOutGenerics(classItem.name)} (" + string.Join(", ", item.parameters.ConvertAll(v => (v.@params ? "params " : string.Empty) + v.type + " " + ChangeName(v.name) + (v.optional ? $" = default({v.type})" : string.Empty))) + ");\n#pragma warning restore CS0824";
                        else
                            endFile += pragmaStart + "\n\t\t" + (item.Dollar ? $"[Name(\"{item.typeAndName.name}\")]\n\t\t" : string.Empty) + (((!(item is ImplicitMethod)) && classItem.type != TypeType.@interface) ? "public " : string.Empty) + interfacePublic + (item.@static || classItem.name == "GlobalClass" ? "static " : "") + item.typeAndName.type + " " + (item.indexer ? "this" : ((item is ImplicitMethod ? ((item as ImplicitMethod).@interface + ".") : ""))) + $"{item.CSName} {item.StartBracket}" + string.Join(", ", item.parameters.ConvertAll(v => (v.@params ? "params " : string.Empty) + v.type + " " + ChangeName(v.name) + (v.optional && !(item is ImplicitMethod) ? $" = default({v.type})" : string.Empty))) + $"{item.EndBracket}{GetWhereString(item.typeWheres)}" + (item.indexer ? " { get; set; }" : ";") + pragmaEnd;
                    }
                if (classItem.type == TypeType.@class && !classItem.@static && !classItem.methods.Any(v => v.typeAndName.name == "constructor") && classItem.name != "GlobalClass")
                    endFile += $"\n#pragma warning disable CS0824\n\t\textern {GetTypeWithOutGenerics(classItem.name)} ();\n#pragma warning restore CS0824";
                /*foreach (var item in classItem.properties)
                    endFile += "\n\t\tpublic " + (item.@static ? "static " : "") + $"extern {item.typeAndName.type} {char.ToUpper(item.typeAndName.name[0])}{item.typeAndName.name.Substring(1)}" + "{ " + (item.get ? "get; " : "") + (item.set ? "set; " : "") + "}";*/
            }
            else if (rItem is EnumDefinition)
            {
                EnumDefinition enumItem = (EnumDefinition)rItem;
                string emitString = enumItem.emit == EnumDefinition.Emit.Value ? "" : "[Enum(Emit." + enumItem.emit + ")]\n\t";
                endFile += $"\t[External]\n\t{emitString}public enum {ChangeName(enumItem.name) + "\n\t{"}\n\t\t{string.Join(",\n\t\t", enumItem.members.ConvertAll(ChangeName))}";
            }
            else if (rItem is Method)
            {
                Method item = (Method)rItem;
                endFile += $"\t[External]\n\tpublic delegate {item.typeAndName.type} {item.name} (" + string.Join(", ", item.parameters.ConvertAll(v => (v.@params ? "params " : string.Empty) + v.type + " " + ChangeName(v.name) + (v.optional ? $" = default({v.type})" : ""))) + ");\n";
                return;
            }

            endFile += "\n\t}\n";
        }

        static void GetMethodsAndFields (ClassDefinition classItem, out List<Field> fields, out List<Method> methods)
        {
            fields = new List<Field>(classItem.fields);
            methods = new List<Method>(classItem.methods);
            var extends = GetExtends(classItem);
                foreach (var item in extends)
                {
                    if (item.type == TypeType.@interface)
                    {
                        foreach (var fieldItem in item.fields)
                            if (!fields.Any(v => v.typeAndName.name == fieldItem.typeAndName.name))
                                fields.Add(fieldItem);
                        foreach (var methodItem in item.methods)
                            if (!methods.Any(v => v.typeAndName.name == methodItem.typeAndName.name && v.typeAndName.type == methodItem.typeAndName.type && v.parameters.SequenceEqual(methodItem.parameters)))
                                if (classItem.methods.Any(v => v.typeAndName.name == methodItem.typeAndName.name))
                                    methods.Add(new ImplicitMethod
                                    {
                                        name = methodItem.name,
                                        @interface = item.name,
                                        parameters = methodItem.parameters,
                                        @static = methodItem.@static,
                                        typeAndName = methodItem.typeAndName,
                                        typeWheres = methodItem.typeWheres
                                    });
                                else
                                    methods.Add(methodItem);
                    }
                }
        }

        static string GetWhereString (KeyValuePair<string, string> value) => $"where {value.Key} : {value.Value}";
        static string GetWhereString(Dictionary<string, string> value)
        {
            if (value.Count == 0) return "";
            else
            {
                string result = "";
                foreach (var item in value)
                    result += " " + GetWhereString(item);
                return result;
            }
        }

        public static List<ClassDefinition> GetExtends(TypeDefinition definition)
        {
            List<ClassDefinition> result = new List<ClassDefinition>();
            ClassDefinition definitionClass = definition as ClassDefinition;
            if (definitionClass != null)
            {
                foreach (var item in definitionClass.extends)
                {
                    var type = FindType(item) as ClassDefinition;
                    if (type != null)
                    {
                        result.AddRange(GetExtends(type));
                        result.Add(type);
                    }
                }
            }
            return result;
        }

        public static TypeDefinition FindType (string name)
        {
            foreach (var @namespace in nameSpaceDefinitions)
                foreach (var type in @namespace.typeDefinitions)
                {
                    var tName = type.name;
                    if (type is Method)
                        tName = (type as Method).typeAndName.name;
                    if (tName == name)
                        return type;
                }
            return null;
        }

        public static string GetType (string value)
        {
            if (value.Length > 1)
                if (value.EndsWith("]") && value[value.Length - 2] != '[') value = value.Substring(0, value.Length - 1);
            if (value.StartsWith("Array<"))
                return GetType(value.Substring(6, value.Length - 7) + "[]");//Array<int>
            else if (value.EndsWith("[]"))
                return GetType(value.Substring(0, value.Length - 2)) + "[]";
            return value;
        }

		/* DEPRECIATED
        static Dictionary<string, string> GenericRead(string tsFile, ref int index, ref string word)
        {
            SkipEmpty(tsFile, ref index);
            bool ext = false;
            Dictionary<string, string> whereTypesExt = new Dictionary<string, string>();
            List<string> typeArguments = new List<string>();
            if (word.Contains('<') && !word.Contains('>'))
            {
                index -= word.Length - word.IndexOf('<');
                word = word.Substring(0, word.IndexOf('<') + 1);
                while (true)
                {
                    var toAdd = SkipToEndOfWord(tsFile, ref index);
                    if (toAdd == "extends" || toAdd == "implements")
                        ext = true;
                    else if (ext)
                    {
                        var last = toAdd.EndsWith(">");
                        whereTypesExt.Add(typeArguments.Last(), last ? toAdd.Substring(0, toAdd.Length - 1) : toAdd);
                        if (last)
                        {
                            word += ">";
                            break;
                        }
                        ext = false;
                    }
                    else
                    {
                        var last = toAdd.EndsWith(">");
                        typeArguments.Add(last ? toAdd.Substring(0, toAdd.Length - 1) : toAdd);
                        word += typeArguments.Last();
                        if (last)
                        {
                            word += ">";
                            break;
                        }
                    }
                }
            }
            return whereTypesExt;
        }
		*/

		static string FirstUpper(string value)
		{
			string result = value;
			if (!String.IsNullOrEmpty(value)) {
				result = char.ToUpper(result[0]) + result.Substring(1);
			}
			return result;
		}

		static string FirstLower(string value)
		{
			string result = value;
			if (!String.IsNullOrEmpty(value)) {
				result = char.ToLower(result[0]) + result.Substring(1);
			}
			return result;
		}

		static string FormatName(string name, string suffix, string generics = null)
		{
			string result = String.Empty;
			if (!String.IsNullOrEmpty(name) && !String.IsNullOrEmpty(suffix)) {
				result = name + suffix;
				int dindex = name.IndexOf("<", StringComparison.Ordinal);
				if (dindex > 0) {
					string dleft = name.Substring(0, dindex);
					string dright = name.Substring(dindex);
					if (!String.IsNullOrEmpty(generics)) {
						dright = "<" + generics + ", " + dright.Substring(1);
					}
					result = String.Format("{0}{1}{2}", dleft, suffix, dright);
				} else {
					if (!String.IsNullOrEmpty(generics)) {
						result = String.Format("{0}{1}<{2}>", name, suffix, generics);
					}
				}
			} else {
				result = name + suffix;
				if (!String.IsNullOrEmpty(generics)) {
					result = String.Format("{0}{1}<{2}>", name, suffix, generics);
				}
			}
			return result;
		}

		internal static string GetTypeGenerics(string value)
		{
			var index = value.IndexOf('<');
			if (index == -1)
				return string.Empty;
			else
				return value.Substring(index);
		}

		internal static string GetTypeGenericStringList(string value)
		{
			string result = String.Empty;
			if (!String.IsNullOrEmpty(value)) {
				int index = value.IndexOf("<", StringComparison.Ordinal);
				int marker = value.LastIndexOf(">", StringComparison.Ordinal);
				if (index >= 0 && marker > index) {
					string insides = value.Substring(index + 1, (marker - index) - 1);
					string[] targs = insides.Split(',');
					foreach (string targ in targs) {
						if (targ.IndexOf("extends", StringComparison.Ordinal) >= 0 || targ.IndexOf("implements", StringComparison.Ordinal) >= 0) {
							string buffer = targ.Replace("extends", "|").Replace("implements", "|");
							string[] items = buffer.Split('|');
							string types = items[0].Trim();
							result += (types.Trim() + ", ");
						} else {
							result += (targ.Trim() + ", ");
						}
					}
					result = result.TrimEnd(',', ' ');
				}
			}
			return result;
		}

		static SkippedWord SkipToEndOfWord(string tsFile, ref int index)
		{
			var result = new SkippedWord();
			if (!char.IsLetter(tsFile, index))
				SkipEmpty(tsFile, ref index);
			if (tsFile[index] == '[')
				return result;
			for (; index < tsFile.Length; index++) {
				var item = tsFile[index];
				if (char.IsLetterOrDigit(item) || item == '[' || item == ']' || item == '<' || item == '>' || item == '_' || item == '.' || item == '$')
					result.Word += item;
				else {
					if (result.Word.EndsWith("]") && !result.Word.EndsWith("[]")) {
						index--;
						result.Word = result.Word.Substring(0, result.Word.Length - 1);
					}
					result.Wheres = ReadGeneric(tsFile, ref index, ref result.Word);
					return result;
				}
			}
			result.Wheres = ReadGeneric(tsFile, ref index, ref result.Word);
			return result;
		}

		static Dictionary<string, string> ReadGeneric(string tsFile, ref int index, ref string word)
		{
			SkipEmpty(tsFile, ref index);
			Dictionary<string, string> whereTypesExt = new Dictionary<string, string>();
			if (word.Contains('<') && !word.Contains('>')) {
				index -= word.Length - word.IndexOf('<');
				word = word.Substring(0, word.IndexOf('<'));
				int marker = CloseGeneric(tsFile, index);
				if (marker > index) {
					string insides = tsFile.Substring(index + 1, (marker - index) - 1);
					word += "<";
					string[] targs = insides.Split(',');
					foreach (string targ in targs) {
						if (targ.IndexOf("extends", StringComparison.Ordinal) >= 0 || targ.IndexOf("implements", StringComparison.Ordinal) >= 0) {
							string buffer = targ.Replace("extends", "|").Replace("implements", "|");
							string[] items = buffer.Split('|');
							string types = items[0].Trim();
							string wheres = items[1].Trim();
							whereTypesExt.Add(types, wheres);
							word += (types.Trim() + ", ");
						} else {
							word += (targ.Trim() + ", ");
						}
					}
					word = word.TrimEnd(',', ' ');
					word += ">";
					index = (marker + 1);
					SkipEmpty(tsFile, ref index);
					// Support Arrays
					char array1 = tsFile[index];
					char array2 = tsFile[index + 1];
					if (array1.Equals('[') && array2.Equals(']')) {
						word += "[]";
						index += 2;
						SkipEmpty(tsFile, ref index);
					}
				} else {
					throw new Exception("Invalid Generic Type Syntax Detected For Word: " + word);
				}
			}
			return whereTypesExt;
		}

		static int CloseGeneric(string tsFile, int index)
		{
			int marker = -1;
			for (; index < tsFile.Length; index++) {
				char check = tsFile[index];
				if (check.Equals('>')) {
					marker = index;
					break;
				}
			}
			return marker;
		}

		private static void ReadTypeScriptFile(string tsFile, ref int index, List<NamespaceDefinition> namespaces)
        {
            NamespaceDefinition global = new NamespaceDefinition();
            namespaces.Add(global);

            List<NamespaceDefinition> namespaceTop = new List<NamespaceDefinition>();
            List<TypeDefinition> typeTop = new List<TypeDefinition> {new ClassDefinition
            {
                name = "GlobalClass",
                type = TypeType.@class
            }
            };
            global.typeDefinitions.Add(typeTop.Last());

            for (; index < tsFile.Length; index++)
            {
                BeginLoop:

                if (index >= tsFile.Length) return;
                SkipEmpty(tsFile, ref index);
                if (index >= tsFile.Length) return;
                while (index < tsFile.Length && tsFile[index] == '/')
                {
                    index++;

                    if (tsFile[index] == '/')
                        index = tsFile.IndexOf('\n', index);
                    else if (tsFile[index] == '*')
                    {
                        index = tsFile.IndexOf("*/", index);
                        index += 2;
                        if (index >= tsFile.Length)
                            return;
                    }

                    SkipEmpty(tsFile, ref index);
                    if (index >= tsFile.Length) return;
                }

                SkipEmpty(tsFile, ref index);

                if (index >= tsFile.Length)
                    break;

                BracketLoop:
                if (tsFile[index] == '}')
                {
                    if (typeTop.Count != 0)
                    {
                        if (typeTop.Last() is ClassDefinition)
                        {
                            if ((typeTop.Last() as ClassDefinition).name == "GlobalClass")
                                goto EndIf;
                        }

                        if (namespaceTop.Count == 0)
                        {
                            global.typeDefinitions.Add(typeTop.Last());
                            typeTop.RemoveAt(typeTop.Count - 1);
                            goto OutIfBreak;
                        }

                        namespaceTop.Last().typeDefinitions.Add(typeTop.Last());
                        typeTop.RemoveAt(typeTop.Count - 1);
                        goto OutIfBreak;
                    }
                    EndIf:

                    if (namespaceTop.Count != 0)
                    {
                        namespaces.Add(namespaceTop.Last());
                        namespaceTop.RemoveAt(namespaceTop.Count - 1);
                        typeTop.RemoveAt(typeTop.Count - 1);
                    }
                    else
                    {
                        Console.WriteLine("No more namespaces. Press enter to continue...");
                        while (Console.ReadKey(true).Key != ConsoleKey.Enter) ;
                    }
                    goto OutIfBreak;
                }

                if (tsFile[index] == '{')
                {
                    index++;
                    SkipEmpty(tsFile, ref index);
                    goto BeginLoop;
                }

                goto After;

                OutIfBreak:
                if (++index >= tsFile.Length)
                    return;
                if (tsFile[index] == '}')
                    goto BeginLoop;
                SkipEmpty(tsFile, ref index);
                if (index >= tsFile.Length) return;
                if (tsFile[index] == ';')
                {
                    index++;
                    SkipEmpty(tsFile, ref index);
                }
                goto BeginLoop;
                After:
                string word;
                bool @static = false;
                bool @abstract = false;
                bool optionalField = false;
                /*bool get = false;
                bool set = false;*/
                SkipEmpty(tsFile, ref index);
				var skip = new SkippedWord();
				do {
					skip = SkipToEndOfWord(tsFile, ref index);
					word = skip.Word;
					switch (word)
                    {
                        case "static":
                        case "function":
                        case "var":
                        case "let":
                        case "const":
                            @static = true;
                            break;
                        case "abstract":
                            @abstract = true;
                            break;
                        /*case "get":
                            get = true;
                            break;
                        case "set":
                            set = true;
                            break;*/
                    }
                    SkipEmpty(tsFile, ref index);
                }
                while (word == "export" || word == "declare" || word == "static" /*|| word == "get" || word == "set"*/ || word == "function" || word == "var" || word == "const" || word == "abstract" || word == "let");
				var whereTypesExt = skip.Wheres; //GenericRead(tsFile, ref index, ref word);
				switch (word)
                {
                    case "type":
                        {
							SkippedWord skipName = SkipToEndOfWord(tsFile, ref index); // Mackey Kinard
							string name = skipName.Word;
							//var wheres = skipName.Wheres; //GenericRead(tsFile, ref index, ref name);
							SkipEmpty(tsFile, ref index);
                            if (tsFile[index++] != '=')
                            {
                                index--;
                                goto default;
                            }
                            SkipEmpty(tsFile, ref index);
                            string type = default(string);
                            if (!ReadFunctionType(tsFile, ref index, ref type, name, typeTop, namespaceTop, global))
                            {
                                List<string> anys = new List<string>();
                                List<string> strings = new List<string>();
                                do
                                {
                                    SkipEmpty(tsFile, ref index);
                                    if (tsFile[index] == '\'' || tsFile[index] == '"')
                                    {
                                        strings.Add(tsFile.Substring(index + 1, tsFile.IndexOf(tsFile[index], index + 1) - index - 1));
                                        anys.Add("string");
                                        index = tsFile.IndexOf(tsFile[index], index + 1) + 1;
                                    }
                                    else
										anys.Add(SkipToEndOfWord(tsFile, ref index).Word);
                                    SkipEmpty(tsFile, ref index);
                                }
                                while (index < tsFile.Length && tsFile[index++] == '|');
                                index--;
                                if (strings.Count == anys.Count)
                                {
                                    var typeDefinitions = namespaceTop.Count == 0 ? global.typeDefinitions : namespaceTop.Last().typeDefinitions;
                                    typeDefinitions.Add(new EnumDefinition
                                    {
                                        name = name,
                                        emit = EnumDefinition.Emit.StringNamePreserveCase,
                                        members = strings
                                    });
                                    type = name;
                                    break;
                                }
                                type = string.Join(", ", anys);
                                if (anys.Count > 1)
                                    type = "Any<" + type + ">";
                            }
                            break;
                        }
                    case "class":
                    case "interface":
                        {
							SkippedWord skipName = SkipToEndOfWord(tsFile, ref index);
							string name = skipName.Word;
							if (string.IsNullOrEmpty(name))
								goto default;
							var wheres = skipName.Wheres; //GenericRead(tsFile, ref index, ref name);
							typeTop.Add(new ClassDefinition
                            {
                                name = name,
                                type = (TypeType)Enum.Parse(typeof(TypeType), word),
                                @abstract = @abstract,
                                typeWheres = wheres
                            });
                            SkipEmpty(tsFile, ref index);

                            string nWord;
							while ((nWord = SkipToEndOfWord(tsFile, ref index).Word) == "extends" || nWord == "implements" || tsFile[index] == ',')
                            {
                                if (tsFile[index] == ',')
                                    index++;
                                SkipEmpty(tsFile, ref index);
                                (typeTop.Last() as ClassDefinition).extends.Add(SkipToEndOfWord(tsFile, ref index).Word);
                                SkipEmpty(tsFile, ref index);
                            }
                            break;
                        }

                    case "enum":
                        typeTop.Add(new EnumDefinition
                        {
                            name = SkipToEndOfWord(tsFile, ref index).Word
                        });
                        break;

                    case "module":
                    case "namespace":
                        if (tsFile[index] == '\'')
                            index++;
						string topNamespace = null;
						var topNameItem = (namespaceTop != null && namespaceTop.Count > 0) ? namespaceTop.Last() : null;
						if (topNameItem != null) {
							topNamespace = topNameItem.name;
						}
						string nameWord = SkipToEndOfWord(tsFile, ref index).Word;
						if (!String.IsNullOrEmpty(topNamespace) && !String.IsNullOrEmpty(nameWord)) {
							nameWord = topNamespace + "." + nameWord;
						}
						namespaceTop.Add(new NamespaceDefinition {
							name = nameWord
						});
						if (string.IsNullOrEmpty(namespaceTop.Last().name))
                        {
                            namespaceTop.RemoveAt(namespaceTop.Count - 1);
                            goto default;
                        }
                        ClassDefinition globalClass = new ClassDefinition
                        {
                            name = "GlobalClass",
                            type = TypeType.@class,
                            @static = true
                        };
                        Array.ForEach(new Action<ClassDefinition>[] { typeTop.Add, namespaceTop.Last().typeDefinitions.Add }, v => v(globalClass));
                        if (tsFile[index] == '\'')
                            index++;
                        break;

                    default:
                        bool optional = tsFile[index] == '?';
                        if (optional)
                        {
                            index++;
                            SkipEmpty(tsFile, ref index);
                        }
                        SkipEmpty(tsFile, ref index);
                        char item = tsFile[index++];
                        switch (item)
                        {
                            case ',':
                            case '}':
                                var enumItem = typeTop.Last() as EnumDefinition;
                                if (enumItem != null)
                                    enumItem.members.Add(word);

                                switch (item)
                                {
                                    case '}':
                                        index--;
                                        goto BracketLoop;
                                    case ',':
                                        goto After;
                                    default:
                                        break;
                                }
                                break;

                            case ':':
                                {
                                    SkipEmpty(tsFile, ref index);
                                    string type = null;
                                    TypeDefinition typeDef;
                                    if (index >= tsFile.Length) return;
									
									var topItem = typeTop.Last();
									string topName = !String.IsNullOrEmpty(topItem.name) ? GetTypeWithOutGenerics(topItem.name) : "GlobalClass";
									string topGens = GetTypeGenericStringList(topItem.name);
									string interfaceName = String.IsNullOrEmpty(word) ? FormatName("Indexer", "Interface", topGens) : FormatName(word, "Interface", topGens);
									string delegateName = FormatName(word, "Delegate", topGens);
									interfaceName = (topName + FirstUpper(interfaceName));
									delegateName = (topName + FirstUpper(delegateName));

									//if (ReadBracketType(tsFile, ref index, (string.IsNullOrEmpty(word) ? typeTop.Last(v => v is ClassDefinition).name + "indexer" : typeTop.Last(v => v is ClassDefinition).name + char.ToUpper(word[0]) + word.Substring(1)) + "Interface", out typeDef, out type))
									if (ReadBracketType(tsFile, ref index, interfaceName, out typeDef, out type))
                                    {
                                        if (namespaceTop.Count == 0)
                                            global.typeDefinitions.Add(typeDef);
                                        else
                                            namespaceTop.Last().typeDefinitions.Add(typeDef);
                                        index++;
                                    }
									else if (!ReadFunctionType(tsFile, ref index, ref type, delegateName /*word + "Delegate"*/, typeTop, namespaceTop, global))
                                    {
                                        List<string> anys = new List<string>();
                                        do
                                        {
                                            SkipEmpty(tsFile, ref index);
                                            anys.Add(SkipToEndOfWord(tsFile, ref index).Word);
                                            SkipEmpty(tsFile, ref index);
                                            if (index + 1 >= tsFile.Length)
                                                return;
                                        }
                                        while (index < tsFile.Length && tsFile[index++] == '|');
                                        index--;
                                        type = string.Join(", ", anys);
                                        if (anys.Count > 1)
                                            type = "Any<" + type + ">";
                                    }
                                    SkipEmpty(tsFile, ref index);
                                    

                                    (typeTop.Last(v => v is ClassDefinition) as ClassDefinition).fields.Add(new Field
                                    {
                                        @static = @static,
                                        from = typeTop.Last(v => v is ClassDefinition) as ClassDefinition,
                                        typeAndName = new TypeNameAndOptional
                                        {
                                            type = type,
                                            name = word,
                                            optional = optionalField
                                        }
                                    });
                                    if (index >= tsFile.Length) return;
                                    if (tsFile[index] == '}')
                                        index--;
                                    continue;
                                }
                            default:
                                continue;

                            case '(':
                            case '[':
                                {
                                    Method method = new Method
                                    {
                                        typeWheres = whereTypesExt,
                                        @static = @static,
                                        indexer = item == '[',
                                        from = typeTop.Last(v => v is ClassDefinition) as ClassDefinition
                                    };
                                    //var startBracket = item; - NOT USED
                                    var endBracket = item == '[' ? ']' : ')';
                                    method.typeAndName.name = word;

                                    SkipEmpty(tsFile, ref index);
                                    if (tsFile[index] == endBracket)
                                    {
                                        index++;
                                        SkipEmpty(tsFile, ref index);
                                        goto Break;
                                    }

                                    for (; index < tsFile.Length; index++)
                                    {
                                        SkipEmpty(tsFile, ref index);
                                        optional = false;
                                        bool @params = false;
                                        if (tsFile[index] == '.')
                                        {
                                            index += 3;
                                            SkipEmpty(tsFile, ref index); @params = true;
                                        }

                                        string word2 = SkipToEndOfWord(tsFile, ref index).Word;
                                        SkipEmpty(tsFile, ref index);

                                        if (tsFile[index] == '?')
                                        {
                                            optional = true;
                                            index++;
                                        }

                                        SkipEmpty(tsFile, ref index);

                                        switch (tsFile[index])
                                        {
                                            case ':':
                                                index++;
                                                SkipEmpty(tsFile, ref index);
                                                string type2 = null;
                                                TypeDefinition typeDef;

												var topItem = typeTop.Last();
												string topName = !String.IsNullOrEmpty(topItem.name) ? GetTypeWithOutGenerics(topItem.name) : "GlobalClass";
												string topGens = GetTypeGenericStringList(topItem.name);
												string interfaceName = FormatName(word2, "Interface", topGens);
												string delegateName = FormatName(method.typeAndName.name, "Param" + (method.parameters.Count + 1).ToString() + "Delegate", topGens);
												interfaceName = (topName + FirstUpper(interfaceName));
												delegateName = (topName + FirstUpper(delegateName));

												if (ReadBracketType(tsFile, ref index, interfaceName /*word2 + "Interface"*/, out typeDef, out type2))
                                                {
                                                    if (namespaceTop.Count == 0)
                                                        global.typeDefinitions.Add(typeDef);
                                                    else
                                                        namespaceTop.Last().typeDefinitions.Add(typeDef);
                                                    index++;
                                                }
												else if (!ReadFunctionType(tsFile, ref index, ref type2, delegateName /*method.typeAndName.name + "Param" + (method.parameters.Count + 1) + "Delegate"*/, typeTop, namespaceTop, global))
                                                {
                                                    List<string> anys = new List<string>();
                                                    do
                                                    {
                                                        SkipEmpty(tsFile, ref index);
                                                        if (tsFile[index] == '\'' || tsFile[index] == '"')
                                                        {
                                                            anys.Add("string");
                                                            index = tsFile.IndexOf(tsFile[index], index + 1) + 1;
                                                        }
                                                        else
                                                            anys.Add(SkipToEndOfWord(tsFile, ref index).Word);
                                                        SkipEmpty(tsFile, ref index);
                                                    }
                                                    while (index < tsFile.Length && tsFile[index++] == '|');
                                                    index--;
                                                    type2 = string.Join(", ", anys);
                                                    if (anys.Count > 1)
                                                        type2 = "Any<" + type2 + ">";
                                                }

                                                method.parameters.Add(new TypeNameOptionalAndParams
                                                {
                                                    optional = optional,
                                                    @params = @params,
                                                    name = word2,
                                                    type = type2
                                                });
                                                SkipEmpty(tsFile, ref index);


                                                

                                                if (tsFile[index] != ',')
                                                    goto case ')';

                                                break;

                                            case ']':
                                            case ')':
                                                index++;
                                                SkipEmpty(tsFile, ref index);
                                                goto Break;
                                        }
                                    }
                                    Break:
                                    string type = "object";
                                    if (tsFile[index] == ':')
                                    {
                                        index++;
                                        SkipEmpty(tsFile, ref index);

                                        TypeDefinition typeDef;

                                        if (index >= tsFile.Length) return;

										var topItem = typeTop.Last();
										string topName = !String.IsNullOrEmpty(topItem.name) ? GetTypeWithOutGenerics(topItem.name) : "GlobalClass";
										string topGens = GetTypeGenericStringList(topItem.name);
										string interfaceName = String.IsNullOrEmpty(word) ? FormatName("Indexer", "Interface", topGens) : FormatName(word, "Interface", topGens);
										string delegateName = FormatName(method.typeAndName.name, "Delegate", topGens);
										interfaceName = (topName + FirstUpper(interfaceName));
										delegateName = (topName + FirstUpper(delegateName));

										//if (ReadBracketType(tsFile, ref index, (string.IsNullOrEmpty(word) ? typeTop.Last().name + "indexerInterface" : typeTop.Last().name + char.ToUpper(word[0]) + word.Substring(1)) + "Interface", out typeDef, out type))
										if (ReadBracketType(tsFile, ref index, interfaceName, out typeDef, out type))
                                        {
                                            if (namespaceTop.Count == 0)
                                                global.typeDefinitions.Add(typeDef);
                                            else
                                                namespaceTop.Last().typeDefinitions.Add(typeDef);
                                            index++;
                                        }
										else if (!ReadFunctionType(tsFile, ref index, ref type, delegateName /*method.typeAndName.name + "Delegate"*/, typeTop, namespaceTop, global))
                                        {
                                            List<string> anys = new List<string>();
                                            do
                                            {
                                                SkipEmpty(tsFile, ref index);
                                                if (tsFile[index] == '\'' || tsFile[index] == '"')
                                                {
                                                    anys.Add("string");
                                                    index = tsFile.IndexOf(tsFile[index], index + 1) + 1;
                                                }
                                                else
                                                    anys.Add(SkipToEndOfWord(tsFile, ref index).Word);
                                                SkipEmpty(tsFile, ref index);
                                            }
                                            while (index < tsFile.Length && tsFile[index++] == '|');
                                            index--;
                                            type = string.Join(", ", anys);
                                            if (anys.Count > 1)
                                                type = "Any<" + type + ">";
                                            method.typeAndName.type = type;
                                            SkipEmpty(tsFile, ref index);
                                    }
                                        method.typeAndName.type = type;
                                        SkipEmpty(tsFile, ref index);
                                    }
                                    else
                                    {
                                        method.typeAndName.type = "object";
                                    }

                                    /*if (get || set)
                                    {
                                        (typeTop.Last() as ClassDefinition).properties.Add(new Property
                                        {
                                            get = get,
                                            set = set,
                                            @static = @static,
                                            typeAndName = method.typeAndName
                                        });
                                    }
                                    else */if (string.IsNullOrEmpty(method.typeAndName.name) && !method.indexer)
                                    {
                                        var oldTypeName = (typeTop.Last(v => v is ClassDefinition) as ClassDefinition).name;
                                        typeTop.RemoveAt(typeTop.Count - 1);
                                        method.typeAndName.name = oldTypeName;
                                        typeTop.Add(method);
                                    }
                                    else
                                    {
                                        (typeTop.Last(v => v is ClassDefinition) as ClassDefinition).methods.Add(method);
                                    }
                                    if (index >= tsFile.Length) return;
                                    if (tsFile[index] == '}')
                                        index--;
                                    goto DoubleBreak;
                                }
                        }
                        break;
                }
                DoubleBreak:;
            }
        }

        public static bool ReadBracketType (string tsFile, ref int index, string typeName, out TypeDefinition typeDefintion, out string type)
        {
            type = "object";
            typeDefintion = null;
            SkipEmpty(tsFile, ref index);
            bool bracket = tsFile[index] == '{';
            if (bracket)
            {
                List<NamespaceDefinition> fakeNamespace = new List<NamespaceDefinition>();
                int fakeIndex = 0;
                int Fn = 0, FIndex = index;
                for (;; FIndex++)
                {
                    switch (tsFile[FIndex])
                    {
                        case '{':
                            Fn++;
                            break;
                        case '}':
                            if (--Fn <= 0)
                                goto DoubleBreakIn;
                            break;
                        default:
                            break;
                    }
                }
                DoubleBreakIn:
                ReadTypeScriptFile(tsFile.Substring(index, FIndex - index), ref fakeIndex, fakeNamespace);
                index += fakeIndex;
                typeDefintion = fakeNamespace[0].typeDefinitions[0];
                type = typeName;
                typeDefintion.name = type;
            }
            return bracket;
        }

        public static bool ReadFunctionType (string tsFile, ref int index, ref string outputType, string delegateName, List<TypeDefinition> typeTop, List<NamespaceDefinition> namespaceTop, NamespaceDefinition global)
        {
            SkipEmpty(tsFile, ref index);
            int i = 0;
            var typeDefinitions = namespaceTop.Count == 0 ? global.typeDefinitions : namespaceTop.Last().typeDefinitions;
            foreach (var item in typeDefinitions)
            {
                if (item is Method && (item as Method).typeAndName.name == delegateName)
                {
					//delegateName = (i == 0 || i == 1 ? delegateName : delegateName.Substring(0, delegateName.Length - i.ToString().Length)) + (i == 0 ? "" : i.ToString());
					delegateName = FormatName(delegateName, "_");
                    i++;
                }
            }
            int oldIndex = index;
			SkippedWord skip = SkipToEndOfWord(tsFile, ref index);
		    //string tWord = skip.Word; - NOT USED
			if (index >= tsFile.Length - 1) {
				index = oldIndex;
				return false;
			}
			var where = skip.Wheres; //GenericRead(tsFile, ref index, ref tWord);
			// NOTE: Dont Remmove '<>' - delegateName = delegateName.Replace(">", "").Replace("<", "");
			List<TypeNameOptionalAndParams> parameters = new List<TypeNameOptionalAndParams>();
            if (tsFile[index] == '(')
            {
                SkipEmpty(tsFile, ref index);
                if (tsFile[++index] == ')')
                {
                    index++;
                    goto EndWhile;
                }
                while (true)
                {
                    SkipEmpty(tsFile, ref index);
                    bool @params = tsFile[index] == '.';
                    if (@params)
                    {
                        index += 3;
                        SkipEmpty(tsFile, ref index);
                    }
					var word = SkipToEndOfWord(tsFile, ref index).Word;
                    SkipEmpty(tsFile, ref index);
                    bool optional = tsFile[index] == '?';
                    if (optional)
                    {
                        index++;
                        SkipEmpty(tsFile, ref index);
                    }
                    index++;
                    SkipEmpty(tsFile, ref index);
                    var type = SkipToEndOfWord(tsFile, ref index).Word;
                    parameters.Add(new TypeNameOptionalAndParams
                    {
                        name = word,
                        type = type,
                        optional = optional,
                        @params = @params
                    });
                    SkipEmpty(tsFile, ref index);
                    var nextItem = tsFile[index++];
                    if (nextItem == ')')
                        break;
                }
                EndWhile:;
            }
            else
            {
                index = oldIndex;
                return false;
            }
            SkipEmpty(tsFile, ref index);
            string returnType = "object";
            if (tsFile[index] == '=')
            {
                index++;
                if (tsFile[index] != '>')
                    goto EndIf;
                index++;
                SkipEmpty(tsFile, ref index);
                returnType = SkipToEndOfWord(tsFile, ref index).Word;
            }
            EndIf:
            typeDefinitions.Add(new Method
            {
                typeAndName = new TypeAndName
                {
                    name = delegateName,
                    type = returnType
                },
                parameters = parameters,
                typeWheres = where
            });
            outputType = delegateName;
            SkipEmpty(tsFile, ref index);
            while (tsFile[index] == '[')
            {
                index += 2;
                outputType += "[]";
            }
            return true;
        }

		/* DEPRECIATED
        private static string SkipToEndOfWord (string tsFile, ref int index)
        {
            if (!char.IsLetter(tsFile, index))
                SkipEmpty(tsFile, ref index);
            if (tsFile[index] == '[')
                return string.Empty;
            string result = "";
            for (; index < tsFile.Length; index++)
            {
                var item = tsFile[index];
                if (char.IsLetterOrDigit(item) || item == '[' || item == ']' || item == '<' || item == '>' || item == '_' || item == '.' || item == '$')
                    result += item;
                else
                {
                    if (result.EndsWith("]") && !result.EndsWith("[]"))
                    {
                        index--;
                        result = result.Substring(0, result.Length - 1);
                    }
                    return result;
                }
            }
            return result;
        }
		*/

        public static string ChangeName (string name)
        {
            return new CSharpCodeProvider().CreateEscapedIdentifier(name);
        }

        private static void SkipEmpty (string tsFile, ref int index)
        {
            for (; index < tsFile.Length; index++)
            {
                switch (tsFile[index])
                {
                    case '\n':
                    case '\r':
                    case '\t':
                    case ' ':
                        break;
                    default:
                        return;
                }
            }
        }
    }
}
