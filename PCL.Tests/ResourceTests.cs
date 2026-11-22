using System.Net;
using System.Text;
using PCL.Kernel;
using PCL.Kernel.Net;
using PCL.Kernel.Resources;
using Xunit;

namespace PCL.Tests;

public sealed class ResourceTests
{
    [Fact]
    public async Task Search_EncodesFacetsAndParsesProjects()
    {
        Uri? requested = null;
        using var handler = new StubHandler(request =>
        {
            requested = request.RequestUri;
            return Json("""
                {"hits":[{"project_id":"AABB","slug":"sodium","title":"Sodium",
                "description":"Rendering optimization","author":"jellysquid3",
                "downloads":12345678,"project_type":"mod",
                "categories":["optimization"],"versions":["1.20.1"]}],
                "total_hits":1}
                """);
        });
        using var client = new ModrinthClient(new HttpClient(handler));

        var result = await client.SearchAsync(
            "sodium", ResourceType.Mod, "1.20.1", "fabric",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.TotalHits);
        var project = Assert.Single(result.Projects);
        Assert.Equal("Sodium", project.Title);
        Assert.Equal("sodium", project.Slug);
        Assert.Equal("jellysquid3", project.Author);
        Assert.Equal(12345678, project.Downloads);
        Assert.Equal("12.3M", project.DownloadsLabel);

        var query = Uri.UnescapeDataString(requested!.Query);
        Assert.Contains("project_type:mod", query);
        Assert.Contains("versions:1.20.1", query);
        Assert.Contains("categories:fabric", query);
    }

    [Fact]
    public async Task GetVersions_ParsesFilesAndDependencies()
    {
        using var handler = new StubHandler(_ => Json("""
            [{"id":"ver1","project_id":"AABB","name":"Sodium 1.0",
            "version_number":"1.0","version_type":"release",
            "game_versions":["1.20.1"],"loaders":["fabric"],
            "files":[{"url":"https://cdn.example/sodium.jar","filename":"sodium.jar",
            "primary":true,"size":1024,"hashes":{"sha1":"abc"}}],
            "dependencies":[
            {"project_id":"IND","dependency_type":"required"},
            {"project_id":"OPT","dependency_type":"optional"}]}]
            """));
        using var client = new ModrinthClient(new HttpClient(handler));

        var versions = await client.GetVersionsAsync(
            "sodium", "1.20.1", "fabric", TestContext.Current.CancellationToken);

        var version = Assert.Single(versions);
        Assert.Equal("Sodium 1.0 · 1.0", version.DisplayName);
        Assert.Equal("https://cdn.example/sodium.jar", version.PrimaryFile!.Url);
        Assert.Equal("abc", version.PrimaryFile.Sha1);
        Assert.Equal(1024, version.PrimaryFile.Size);
        Assert.True(Assert.Single(version.Dependencies, d => d.IsRequired).IsRequired);
        Assert.Equal(2, version.Dependencies.Count);
    }

    [Fact]
    public async Task ResolveRequiredDependencies_WalksChainAndSkipsOptional()
    {
        using var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/project/FAPI/version"))
                return Json("""
                    [{"id":"api-1","project_id":"FAPI","name":"Fabric API",
                    "version_number":"0.9","version_type":"release",
                    "game_versions":["1.20.1"],"loaders":["fabric"],
                    "files":[{"url":"https://cdn.example/fabric-api.jar",
                    "filename":"fabric-api.jar","primary":true,"size":2048}],
                    "dependencies":[{"project_id":"CORE","dependency_type":"required"}]}]
                    """);

            if (path.EndsWith("/project/CORE/version"))
                return Json("""
                    [{"id":"core-1","project_id":"CORE","name":"Core Lib",
                    "version_number":"1.0","version_type":"release",
                    "game_versions":["1.20.1"],"loaders":["fabric"],
                    "files":[{"url":"https://cdn.example/core.jar",
                    "filename":"core.jar","primary":true,"size":512}],
                    "dependencies":[]}]
                    """);

            return Json("[]");
        });
        using var client = new ModrinthClient(new HttpClient(handler));

        var root = new ResourceVersion(
            "root-1", "ROOT", "Root Mod", "1.0", "release",
            ["1.20.1"], ["fabric"],
            [new ResourceFile("https://cdn.example/root.jar", "root.jar", true, 4096, null)],
            [
                new ResourceDependency(null, "FAPI", "required"),
                new ResourceDependency("opt-1", "OPT", "optional")
            ]);

        var dependencies = await client.ResolveRequiredDependenciesAsync(
            root, "1.20.1", "fabric", TestContext.Current.CancellationToken);

        Assert.Equal(2, dependencies.Count);
        Assert.Equal("FAPI", dependencies[0].ProjectId);
        Assert.Equal("CORE", dependencies[1].ProjectId);
        Assert.DoesNotContain(dependencies, item => item.ProjectId == "OPT");
    }

    [Fact]
    public void TargetDirectory_MapsEveryResourceType()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pcl-resource-test-" +
                                                          Guid.NewGuid().ToString("N"));
        var paths = new LauncherPaths(directory, Path.Combine(directory, "game"));

        using var downloads = new DownloadClient();
        using var modrinth = new ModrinthClient();
        var installer = new ResourceInstaller(paths, downloads, modrinth);

        Assert.Equal(Path.Combine(paths.GameDirectory, "mods"),
            installer.TargetDirectory(ResourceType.Mod));
        Assert.Equal(Path.Combine(paths.GameDirectory, "resourcepacks"),
            installer.TargetDirectory(ResourceType.ResourcePack));
        Assert.Equal(Path.Combine(paths.GameDirectory, "shaderpacks"),
            installer.TargetDirectory(ResourceType.Shader));
        Assert.Equal(Path.Combine(paths.GameDirectory, "datapacks"),
            installer.TargetDirectory(ResourceType.DataPack));
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
