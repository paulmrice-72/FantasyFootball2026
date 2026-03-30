// FF.Tests/Architecture/ArchitectureTests.cs
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace FF.Tests.Architecture;

public class ArchitectureTests
{
    private const string DomainAssembly = "FF.Domain";
    private const string ApplicationAssembly = "FF.Application";
    private const string InfrastructureAssembly = "FF.Infrastructure";
    private const string ApiAssembly = "FF.API";

    // ── 1. Domain has no outward dependencies ─────────────────────────────

    [Fact]
    public void Domain_Should_Not_Reference_Application()
    {
        var result = Types.InAssembly(typeof(FF.Domain.AssemblyMarker).Assembly)
            .Should()
            .NotHaveDependencyOn(ApplicationAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "FF.Domain must not reference FF.Application");
    }

    [Fact]
    public void Domain_Should_Not_Reference_Infrastructure()
    {
        var result = Types.InAssembly(typeof(FF.Domain.AssemblyMarker).Assembly)
            .Should()
            .NotHaveDependencyOn(InfrastructureAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "FF.Domain must not reference FF.Infrastructure");
    }

    [Fact]
    public void Domain_Should_Not_Reference_Api()
    {
        var result = Types.InAssembly(typeof(FF.Domain.AssemblyMarker).Assembly)
            .Should()
            .NotHaveDependencyOn(ApiAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "FF.Domain must not reference FF.API");
    }

    // ── 2. Application does not reference Infrastructure or API ───────────

    [Fact]
    public void Application_Should_Not_Reference_Infrastructure()
    {
        var result = Types.InAssembly(typeof(FF.Application.AssemblyMarker).Assembly)
            .Should()
            .NotHaveDependencyOn(InfrastructureAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "FF.Application must not reference FF.Infrastructure");
    }

    [Fact]
    public void Application_Should_Not_Reference_Api()
    {
        var result = Types.InAssembly(typeof(FF.Application.AssemblyMarker).Assembly)
            .Should()
            .NotHaveDependencyOn(ApiAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "FF.Application must not reference FF.API");
    }

    // ── 3. Infrastructure does not reference API ──────────────────────────

    [Fact]
    public void Infrastructure_Should_Not_Reference_Api()
    {
        var result = Types.InAssembly(typeof(FF.Infrastructure.AssemblyMarker).Assembly)
            .Should()
            .NotHaveDependencyOn(ApiAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "FF.Infrastructure must not reference FF.API");
    }

    // ── 4. Controllers live only in FF.API ────────────────────────────────
    // Check the three inner layers — none should contain a ControllerBase subclass

    [Fact]
    public void Domain_Should_Have_No_Controllers()
    {
        var types = Types.InAssembly(typeof(FF.Domain.AssemblyMarker).Assembly)
            .That()
            .Inherit(typeof(Microsoft.AspNetCore.Mvc.ControllerBase))
            .GetTypes();

        types.Should().BeEmpty(
            because: "FF.Domain must not contain controllers");
    }

    [Fact]
    public void Application_Should_Have_No_Controllers()
    {
        var types = Types.InAssembly(typeof(FF.Application.AssemblyMarker).Assembly)
            .That()
            .Inherit(typeof(Microsoft.AspNetCore.Mvc.ControllerBase))
            .GetTypes();

        types.Should().BeEmpty(
            because: "FF.Application must not contain controllers");
    }

    [Fact]
    public void Infrastructure_Should_Have_No_Controllers()
    {
        var types = Types.InAssembly(typeof(FF.Infrastructure.AssemblyMarker).Assembly)
            .That()
            .Inherit(typeof(Microsoft.AspNetCore.Mvc.ControllerBase))
            .GetTypes();

        types.Should().BeEmpty(
            because: "FF.Infrastructure must not contain controllers");
    }

    // ── 5. MediatR handlers live only in FF.Application ──────────────────

    [Fact]
    public void Infrastructure_Should_Have_No_MediatR_Handlers()
    {
        var types = Types.InAssembly(typeof(FF.Infrastructure.AssemblyMarker).Assembly)
            .That()
            .ImplementInterface(typeof(MediatR.IRequestHandler<,>))
            .GetTypes();

        types.Should().BeEmpty(
            because: "MediatR handlers must not exist in FF.Infrastructure");
    }
}