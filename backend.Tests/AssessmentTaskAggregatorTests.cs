using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using backend.Models;
using backend.Services;
using Xunit;

namespace backend.Tests;

public class AssessmentTaskAggregatorTests
{
    private readonly ProjectAssessment _sampleAssessment;
    private readonly PresalesConfiguration _configuration;

    public AssessmentTaskAggregatorTests()
    {
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "Samples", "assessment-sample.json");
        var json = File.ReadAllText(jsonPath);
        _sampleAssessment = JsonSerializer.Deserialize<ProjectAssessment>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Failed to load sample assessment JSON.");

        _configuration = new PresalesConfiguration
        {
            ItemActivities = new List<ItemActivityMapping>
            {
                new ItemActivityMapping { ItemName = "Requirement & Documentation", ActivityName = "Analysis & Design" },
                new ItemActivityMapping { ItemName = "Architect Setup", ActivityName = "Architecture & Setup" },
                new ItemActivityMapping { ItemName = "BE Development", ActivityName = "Application Development" },
                new ItemActivityMapping { ItemName = "FE Development", ActivityName = "Application Development" },
                new ItemActivityMapping { ItemName = "SIT (Manual by QA)", ActivityName = "Testing & QA" },
                new ItemActivityMapping { SectionName = "Project Preparation", ActivityName = "Project Preparation" }
            },
            EstimationColumnRoles = new List<EstimationColumnRoleMapping>
            {
                new EstimationColumnRoleMapping { EstimationColumn = "Business Analyst", RoleName = "Business Analyst" },
                new EstimationColumnRoleMapping { EstimationColumn = "Requirement & Documentation", RoleName = "Business Analyst" },
                new EstimationColumnRoleMapping { EstimationColumn = "Architect Setup", RoleName = "Architect" },
                new EstimationColumnRoleMapping { EstimationColumn = "BE Development", RoleName = "Developer" },
                new EstimationColumnRoleMapping { EstimationColumn = "FE Development", RoleName = "Developer" },
                new EstimationColumnRoleMapping { EstimationColumn = "SIT (Manual by QA)", RoleName = "Quality Engineer" }
            }
        };
    }

    [Fact]
    public void AggregateEstimationColumnEffort_ComputesExpectedManDays()
    {
        var result = AssessmentTaskAggregator.AggregateEstimationColumnEffort(_sampleAssessment);

        Assert.Equal(6, result.Count);
        AssertClose(2, result.GetValueOrDefault("Business Analyst"));
        AssertClose(6, result.GetValueOrDefault("Requirement & Documentation"));
        AssertClose(3, result.GetValueOrDefault("Architect Setup"));
        AssertClose(10, result.GetValueOrDefault("BE Development"));
        AssertClose(5, result.GetValueOrDefault("FE Development"));
        AssertClose(2, result.GetValueOrDefault("SIT (Manual by QA)"));
    }

    [Fact]
    public void CalculateRoleManDays_RespectsRoleMappings()
    {
        var result = AssessmentTaskAggregator.CalculateRoleManDays(_sampleAssessment, _configuration);

        Assert.Equal(4, result.Count);
        AssertClose(8, result.GetValueOrDefault("Business Analyst"));
        AssertClose(3, result.GetValueOrDefault("Architect"));
        AssertClose(15, result.GetValueOrDefault("Developer"));
        AssertClose(2, result.GetValueOrDefault("Quality Engineer"));
    }

    [Fact]
    public void CalculateActivityManDays_UsesColumnMappingsFirst()
    {
        var result = AssessmentTaskAggregator.CalculateActivityManDays(_sampleAssessment, _configuration);

        Assert.Equal(5, result.Count);
        AssertClose(2, result.GetValueOrDefault("Project Preparation"));
        AssertClose(6, result.GetValueOrDefault("Analysis & Design"));
        AssertClose(3, result.GetValueOrDefault("Architecture & Setup"));
        AssertClose(15, result.GetValueOrDefault("Development"));
        AssertClose(2, result.GetValueOrDefault("Testing & QA"));
    }

    [Fact]
    public void GetGanttTasks_GroupsEffortPerAssessmentItem()
    {
        var tasks = AssessmentTaskAggregator.GetGanttTasks(_sampleAssessment, _configuration);

        Assert.Equal(6, tasks.Count);

        var backendDev = Assert.Single(tasks, t => t.Detail == "BE Development");
        Assert.Equal("Application Development", backendDev.ActivityGroup);
        Assert.Equal("Developer", backendDev.Actor);
        AssertClose(10, backendDev.ManDays);

        var sitTask = Assert.Single(tasks, t => t.Detail == "SIT (Manual by QA)");
        Assert.Equal("Testing & QA", sitTask.ActivityGroup);
        Assert.Equal("Quality Engineer", sitTask.Actor);
        AssertClose(2, sitTask.ManDays);
    }

    private static void AssertClose(double expected, double actual, double tolerance = 1e-6)
    {
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }
}
