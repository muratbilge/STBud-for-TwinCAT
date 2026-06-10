using STFormatter.Core.IoTree;

namespace STFormatter.Core.Tests;

public class IoTreeParserTests : IDisposable
{
    private readonly string _dir;

    public IoTreeParserTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "STBudTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WriteTsproj(string content, string name = "Demo.tsproj")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string SampleTsproj = """
        <?xml version="1.0"?>
        <TcSmProject>
          <Project>
            <Io>
              <Device>
                <Name>Device 1 (EtherCAT)</Name>
                <Box>
                  <Name>Term 1 (EK1100)</Name>
                  <EtherCAT Type="EK1100" Desc="EK1100 EtherCAT Coupler">
                    <Pdo Name="Channel 1" SyncMan="3" Index="#x1a00">
                      <Entry Name="Input" Sub="1">
                        <Type>BOOL</Type>
                      </Entry>
                      <Entry Name="Status__WcState" Sub="2">
                        <Type>BIT</Type>
                      </Entry>
                    </Pdo>
                    <Pdo Name="Channel 2" InOut="1">
                      <Entry Name="Output" Sub="1">
                        <Type>BOOL</Type>
                      </Entry>
                    </Pdo>
                  </EtherCAT>
                  <Box>
                    <Name>Term 2 (EL2004)</Name>
                    <EtherCAT Type="EL2004" Desc="EL2004 4Ch. Dig. Output" />
                  </Box>
                </Box>
              </Device>
            </Io>
            <Mappings>
              <OwnerA Name="TIPC^PlcTask Inputs">
                <OwnerB Name="TIID^Device 1 (EtherCAT)^Term 1 (EK1100)">
                  <Link VarA="MAIN.bInput" VarB="Channel 1^Input" />
                </OwnerB>
              </OwnerA>
            </Mappings>
          </Project>
        </TcSmProject>
        """;

    [Fact]
    public void ParseIoTree_BuildsDeviceBoxHierarchyWithTiidPaths()
    {
        var root = IoTreeParser.ParseIoTree(WriteTsproj(SampleTsproj));

        Assert.NotNull(root);
        Assert.Equal("Root", root!.NodeType);
        var device = Assert.Single(root.Children);
        Assert.Equal("Device 1 (EtherCAT)", device.Name);
        Assert.Equal("TIID^Device 1 (EtherCAT)", device.Path);

        var box = Assert.Single(device.Children);
        Assert.Equal("Term 1 (EK1100)", box.Name);
        Assert.Equal("TIID^Device 1 (EtherCAT)^Term 1 (EK1100)", box.Path);
        Assert.Equal("EK1100 EtherCAT Coupler (EK1100)", box.Description);
    }

    [Fact]
    public void ParseIoTree_NestedBoxAndPdosAreChildrenOfParentBox()
    {
        var root = IoTreeParser.ParseIoTree(WriteTsproj(SampleTsproj));
        var box = root!.Children[0].Children[0];

        // Two PDOs from the EtherCAT element plus the nested box
        Assert.Equal(3, box.Children.Count);
        Assert.Equal("Pdo", box.Children[0].NodeType);
        Assert.Equal("Pdo", box.Children[1].NodeType);
        var subBox = box.Children[2];
        Assert.Equal("Box", subBox.NodeType);
        Assert.Equal("TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL2004)", subBox.Path);
    }

    [Fact]
    public void ParseIoTree_DirectionFromSyncManAndInOut()
    {
        var root = IoTreeParser.ParseIoTree(WriteTsproj(SampleTsproj));
        var box = root!.Children[0].Children[0];

        Assert.Equal("Input", box.Children[0].Direction);   // SyncMan="3" (odd)
        Assert.Equal("Output", box.Children[1].Direction);  // InOut="1"
    }

    [Fact]
    public void ParseIoTree_EntryNamesMapDoubleUnderscoreToCaret()
    {
        var root = IoTreeParser.ParseIoTree(WriteTsproj(SampleTsproj));
        var pdo = root!.Children[0].Children[0].Children[0];

        Assert.Equal(2, pdo.Children.Count);
        Assert.Equal("Input", pdo.Children[0].Name);
        Assert.Equal("Status^WcState", pdo.Children[1].Name);
        Assert.Equal("TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Channel 1^Status^WcState",
            pdo.Children[1].Path);
    }

    [Fact]
    public void ParseIoTree_MissingFileOrNoIoSectionReturnsNull()
    {
        Assert.Null(IoTreeParser.ParseIoTree(Path.Combine(_dir, "missing.tsproj")));
        Assert.Null(IoTreeParser.ParseIoTree(WriteTsproj("<TcSmProject><Project/></TcSmProject>")));
        Assert.Null(IoTreeParser.ParseIoTree(WriteTsproj("not xml at all", "bad.tsproj")));
    }

    [Fact]
    public void ParseMappings_ReadsLinkAttributes()
    {
        var mappings = IoTreeParser.ParseMappings(WriteTsproj(SampleTsproj));

        var m = Assert.Single(mappings);
        Assert.Equal("TIID^Device 1 (EtherCAT)^Term 1 (EK1100)", m.IoPath);
        Assert.Equal("MAIN.bInput", m.PlcVariable);
        Assert.Equal("Channel 1^Input", m.ChannelName);
    }

    [Fact]
    public void ParseMappings_MissingFileReturnsEmpty()
    {
        Assert.Empty(IoTreeParser.ParseMappings(Path.Combine(_dir, "missing.tsproj")));
    }

    [Fact]
    public void FindTsprojFile_FindsFileNextToSolutionAndInSubdirectories()
    {
        var sln = Path.Combine(_dir, "My.sln");
        File.WriteAllText(sln, "");

        Assert.Null(IoTreeParser.FindTsprojFile(sln));

        var sub = Path.Combine(_dir, "TwinCAT Project");
        Directory.CreateDirectory(sub);
        var tsproj = Path.Combine(sub, "Plc.tsproj");
        File.WriteAllText(tsproj, SampleTsproj);

        Assert.Equal(tsproj, IoTreeParser.FindTsprojFile(sln));
    }
}
