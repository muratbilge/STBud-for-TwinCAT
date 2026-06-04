using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace STFormatter.Core.IoTree
{
    public class IoTreeParser
    {
        public static IoTreeNode? ParseIoTree(string tsprojPath)
        {
            if (string.IsNullOrEmpty(tsprojPath) || !File.Exists(tsprojPath))
                return null;

            try
            {
                var doc = new XmlDocument();
                doc.Load(tsprojPath);

                var ioNode = doc.SelectSingleNode("//Io");
                if (ioNode == null)
                    return null;

                var root = new IoTreeNode
                {
                    Name = "I/O",
                    NodeType = "Root",
                    Path = ""
                };

                var devices = ioNode.SelectNodes("Device");
                if (devices == null || devices.Count == 0)
                    return null;

                foreach (XmlElement device in devices)
                {
                    var deviceNode = ParseDevice(device);
                    if (deviceNode != null)
                        root.Children.Add(deviceNode);
                }

                return root.Children.Count > 0 ? root : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static List<IoMapping> ParseMappings(string tsprojPath)
        {
            var mappings = new List<IoMapping>();

            if (string.IsNullOrEmpty(tsprojPath) || !File.Exists(tsprojPath))
                return mappings;

            try
            {
                var doc = new XmlDocument();
                doc.Load(tsprojPath);

                var mappingNodes = doc.SelectNodes("//Mappings/OwnerA/OwnerB/Link");
                if (mappingNodes == null)
                    return mappings;

                foreach (XmlElement link in mappingNodes)
                {
                    var ownerB = link.ParentNode as XmlElement;
                    if (ownerB == null) continue;

                    mappings.Add(new IoMapping
                    {
                        IoPath = ownerB.GetAttribute("Name"),
                        PlcVariable = link.GetAttribute("VarA"),
                        ChannelName = link.GetAttribute("VarB")
                    });
                }
            }
            catch { }

            return mappings;
        }

        public static string? FindTsprojFile(string solutionPath)
        {
            if (string.IsNullOrEmpty(solutionPath) || !File.Exists(solutionPath))
                return null;

            try
            {
                string dir = Path.GetDirectoryName(solutionPath) ?? "";
                if (string.IsNullOrEmpty(dir))
                    return null;

                var files = Directory.GetFiles(dir, "*.tsproj", SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                    return files[0];

                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    files = Directory.GetFiles(subDir, "*.tsproj", SearchOption.TopDirectoryOnly);
                    if (files.Length > 0)
                        return files[0];
                }

                var allFiles = Directory.GetFiles(dir, "*.tsproj", SearchOption.AllDirectories);
                return allFiles.Length > 0 ? allFiles.FirstOrDefault(f =>
                {
                    try { new XmlDocument().Load(f); return true; }
                    catch { return false; }
                }) : null;
            }
            catch
            {
                return null;
            }
        }

        private static IoTreeNode ParseDevice(XmlElement deviceEl)
        {
            string name = GetChildText(deviceEl, "Name");
            if (string.IsNullOrEmpty(name))
                name = deviceEl.GetAttribute("RemoteName");

            var path = $"TIID^{name}";
            var node = new IoTreeNode
            {
                Name = name,
                NodeType = "Device",
                Path = path,
                Description = ""
            };

            var boxes = deviceEl.SelectNodes("Box");
            if (boxes != null)
            {
                string parentPath = path;
                foreach (XmlElement box in boxes)
                {
                    var boxNode = ParseBox(box, parentPath);
                    if (boxNode != null)
                        node.Children.Add(boxNode);
                }
            }

            return node;
        }

        private static IoTreeNode? ParseBox(XmlElement boxEl, string parentPath)
        {
            string name = GetChildText(boxEl, "Name");
            if (string.IsNullOrEmpty(name))
                return null;

            var path = $"{parentPath}^{name}";
            string description = "";

            var etherCatEl = boxEl["EtherCAT"];
            if (etherCatEl != null)
            {
                string type = etherCatEl.GetAttribute("Type");
                string desc = etherCatEl.GetAttribute("Desc");
                if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(desc) && desc != type)
                    description = $"{desc} ({type})";
                else if (!string.IsNullOrEmpty(desc))
                    description = desc;
                else if (!string.IsNullOrEmpty(type))
                    description = type;
            }

            var node = new IoTreeNode
            {
                Name = name,
                NodeType = "Box",
                Path = path,
                Description = description
            };

            var pdos = boxEl.SelectNodes("Pdo");
            if (pdos != null)
            {
                foreach (XmlElement pdo in pdos)
                {
                    var pdoNode = ParsePdo(pdo, path, direction: "");
                    if (pdoNode != null)
                        node.Children.Add(pdoNode);
                }
            }

            if (etherCatEl != null)
            {
                var ecPdos = etherCatEl.SelectNodes("Pdo");
                if (ecPdos != null)
                {
                    foreach (XmlElement pdo in ecPdos)
                    {
                        var pdoNode = ParsePdo(pdo, path, direction: "");
                        if (pdoNode != null)
                            node.Children.Add(pdoNode);
                    }
                }
            }

            var subBoxes = boxEl.SelectNodes("Box");
            if (subBoxes != null)
            {
                foreach (XmlElement subBox in subBoxes)
                {
                    var subNode = ParseBox(subBox, path);
                    if (subNode != null)
                        node.Children.Add(subNode);
                }
            }

            return node;
        }

        private static IoTreeNode? ParsePdo(XmlElement pdoEl, string parentPath, string direction = "")
        {
            string name = pdoEl.GetAttribute("Name");
            if (string.IsNullOrEmpty(name))
                return null;

            string flags = pdoEl.GetAttribute("Flags");
            string syncMan = pdoEl.GetAttribute("SyncMan");
            string inOut = pdoEl.GetAttribute("InOut");
            string dir = DetermineDirection(flags, syncMan, inOut);
            if (string.IsNullOrEmpty(dir))
                dir = direction;
            string index = pdoEl.GetAttribute("Index");

            var path = $"{parentPath}^{name}";

            var node = new IoTreeNode
            {
                Name = name,
                NodeType = "Pdo",
                Path = path,
                Description = $"{dir}",
                Direction = dir,
            };

            if (!string.IsNullOrEmpty(index))
                node.Description = $"{dir} [{index}]";

            var entries = pdoEl.SelectNodes("Entry");
            if (entries != null)
            {
                foreach (XmlElement entry in entries)
                {
                    var entryNode = ParseEntry(entry, path);
                    if (entryNode != null)
                        node.Children.Add(entryNode);
                }
            }

            return node;
        }

        private static IoTreeNode? ParseEntry(XmlElement entryEl, string parentPath)
        {
            string rawName = entryEl.GetAttribute("Name");
            if (string.IsNullOrEmpty(rawName))
                return null;

            string name = rawName.Replace("__", "^");

            if (name.EndsWith("^") || string.IsNullOrWhiteSpace(name.TrimEnd('^')))
                return null;

            string sub = entryEl.GetAttribute("Sub");
            string flags = entryEl.GetAttribute("Flags");

            bool hasSubIndex = !string.IsNullOrEmpty(sub);
            bool isMappable = hasSubIndex || (flags.Length > 0 && flags != "#x00000000");

            var typeEl = entryEl["Type"];
            string typeName = typeEl?.InnerText?.Trim() ?? "";
            bool isArrayType = typeName.IndexOf("ARRAY", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isMappable && isArrayType)
                return null;
            if (!hasSubIndex && isArrayType)
                return null;

            string path = $"{parentPath}^{name}";
            string description = "";

            if (!string.IsNullOrEmpty(sub))
                description = $"Sub {sub}";

            if (!string.IsNullOrEmpty(typeName) && !isArrayType)
                description = string.IsNullOrEmpty(description) ? typeName : $"{typeName} (Sub {sub})";

            return new IoTreeNode
            {
                Name = name,
                NodeType = "Entry",
                Path = path,
                Description = description
            };
        }

        private static string DetermineDirection(string flags, string syncMan, string inOut = "")
        {
            if (!string.IsNullOrEmpty(inOut))
            {
                if (inOut == "1")
                    return "Output";
                if (inOut == "0")
                    return "Input";
            }

            if (!string.IsNullOrEmpty(syncMan))
            {
                if (int.TryParse(syncMan, out int sm))
                {
                    return (sm % 2 == 0) ? "Output" : "Input";
                }
            }

            if (!string.IsNullOrEmpty(flags))
            {
                try
                {
                    if (flags.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
                    {
                        string hex = flags.Substring(2);
                        int flagVal = Convert.ToInt32(hex, 16);
                        if ((flagVal & 0x01) != 0)
                            return "Input";
                        if ((flagVal & 0x02) != 0)
                            return "Output";
                    }
                }
                catch { }
            }

            return "";
        }

        private static string GetChildText(XmlElement parent, string childName)
        {
            var child = parent[childName];
            return child?.InnerText?.Trim() ?? "";
        }
    }

    public class IoMapping
    {
        public string IoPath { get; set; } = "";
        public string PlcVariable { get; set; } = "";
        public string ChannelName { get; set; } = "";
    }
}