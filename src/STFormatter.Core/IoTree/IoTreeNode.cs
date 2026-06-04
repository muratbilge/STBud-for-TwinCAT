using System.Collections.Generic;

namespace STFormatter.Core.IoTree
{
    public class IoTreeNode
    {
        public string Name { get; set; } = "";
        public string NodeType { get; set; } = "";
        public string Path { get; set; } = "";
        public string Description { get; set; } = "";
        public string Direction { get; set; } = "";
        public List<IoTreeNode> Children { get; } = new List<IoTreeNode>();

        public bool HasChildren => Children.Count > 0;

        public string DisplayText
        {
            get
            {
                if (NodeType == "Pdo" && !string.IsNullOrEmpty(Direction))
                {
                    var tag = Direction switch
                    {
                        "Input" => "[I]",
                        "Output" => "[O]",
                        _ => ""
                    };
                    if (string.IsNullOrEmpty(Description) || Description == Direction)
                        return $"{tag} {Name}";
                    return $"{tag} {Name}  ({Description})";
                }
                if (string.IsNullOrEmpty(Description))
                    return Name;
                return $"{Name}  ({Description})";
            }
        }
    }
}