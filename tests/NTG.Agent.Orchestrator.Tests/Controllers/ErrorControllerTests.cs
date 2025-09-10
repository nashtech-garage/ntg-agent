using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Moq;
using NTG.Agent.Orchestrator.Controllers;

namespace NTG.Agent.Orchestrator.Tests.Controllers;

[TestFixture]
public class ErrorControllerTests
{
    private ErrorController _controller;
    private Mock<IHostEnvironment> _mockHostEnvironment;

    [SetUp]
    public void Setup()
    {
        _mockHostEnvironment = new Mock<IHostEnvironment>();
        _controller = new ErrorController();
        
        // Set up proper ControllerContext for Problem() method to work correctly
        var mockHttpContext = new Mock<HttpContext>();
        mockHttpContext.Setup(x => x.Request.Scheme).Returns("https");
        mockHttpContext.Setup(x => x.Request.Host).Returns(new HostString("localhost"));
        mockHttpContext.Setup(x => x.Request.PathBase).Returns(new PathString(""));
        
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = mockHttpContext.Object
        };
    }

    #region HandleErrorDevelopment Tests

    [Test]
    public void HandleErrorDevelopment_WhenNotDevelopmentEnvironment_ReturnsNotFound()
    {
        // Arrange
        _mockHostEnvironment.Setup(x => x.EnvironmentName).Returns("Production");

        // Act
        var result = _controller.HandleErrorDevelopment(_mockHostEnvironment.Object);

        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public void HandleErrorDevelopment_WhenDevelopmentEnvironment_ReturnsProblemDetails()
    {
        // Arrange
        _mockHostEnvironment.Setup(x => x.EnvironmentName).Returns("Development");

        var testException = new InvalidOperationException("Test exception message");
        var mockExceptionFeature = new Mock<IExceptionHandlerFeature>();
        mockExceptionFeature.Setup(x => x.Error).Returns(testException);

        var mockFeatureCollection = new Mock<IFeatureCollection>();
        mockFeatureCollection.Setup(x => x.Get<IExceptionHandlerFeature>())
            .Returns(mockExceptionFeature.Object);

        var mockHttpContext = new Mock<HttpContext>();
        mockHttpContext.Setup(x => x.Features).Returns(mockFeatureCollection.Object);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = mockHttpContext.Object
        };

        // Act
        var result = _controller.HandleErrorDevelopment(_mockHostEnvironment.Object);

        // Assert
        Assert.That(result, Is.TypeOf<ObjectResult>());
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult.StatusCode, Is.EqualTo(500));

        var problemDetails = objectResult.Value as ProblemDetails;
        Assert.That(problemDetails, Is.Not.Null);
        Assert.That(problemDetails.Title, Is.EqualTo("Test exception message"));
        Assert.That(problemDetails.Detail, Is.EqualTo(testException.StackTrace));
    }

    [Test]
    public void HandleErrorDevelopment_WhenDevelopmentEnvironmentWithNullStackTrace_ReturnsProblemDetails()
    {
        // Arrange
        _mockHostEnvironment.Setup(x => x.EnvironmentName).Returns("Development");

        var testException = new Exception("Test exception message");
        var mockExceptionFeature = new Mock<IExceptionHandlerFeature>();
        mockExceptionFeature.Setup(x => x.Error).Returns(testException);

        var mockFeatureCollection = new Mock<IFeatureCollection>();
        mockFeatureCollection.Setup(x => x.Get<IExceptionHandlerFeature>())
            .Returns(mockExceptionFeature.Object);

        var mockHttpContext = new Mock<HttpContext>();
        mockHttpContext.Setup(x => x.Features).Returns(mockFeatureCollection.Object);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = mockHttpContext.Object
        };

        // Act
        var result = _controller.HandleErrorDevelopment(_mockHostEnvironment.Object);

        // Assert
        Assert.That(result, Is.TypeOf<ObjectResult>());
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult.StatusCode, Is.EqualTo(500));

        var problemDetails = objectResult.Value as ProblemDetails;
        Assert.That(problemDetails, Is.Not.Null);
        Assert.That(problemDetails.Title, Is.EqualTo("Test exception message"));
        Assert.That(problemDetails.Detail, Is.Null); // StackTrace is null for new Exception()
    }

    [Test]
    public void HandleErrorDevelopment_WhenDevelopmentEnvironmentWithInnerException_ReturnsProblemDetails()
    {
        // Arrange
        _mockHostEnvironment.Setup(x => x.EnvironmentName).Returns("Development");

        var innerException = new ArgumentException("Inner exception");
        var testException = new InvalidOperationException("Outer exception", innerException);
        var mockExceptionFeature = new Mock<IExceptionHandlerFeature>();
        mockExceptionFeature.Setup(x => x.Error).Returns(testException);

        var mockFeatureCollection = new Mock<IFeatureCollection>();
        mockFeatureCollection.Setup(x => x.Get<IExceptionHandlerFeature>())
            .Returns(mockExceptionFeature.Object);

        var mockHttpContext = new Mock<HttpContext>();
        mockHttpContext.Setup(x => x.Features).Returns(mockFeatureCollection.Object);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = mockHttpContext.Object
        };

        // Act
        var result = _controller.HandleErrorDevelopment(_mockHostEnvironment.Object);

        // Assert
        Assert.That(result, Is.TypeOf<ObjectResult>());
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult.StatusCode, Is.EqualTo(500));

        var problemDetails = objectResult.Value as ProblemDetails;
        Assert.That(problemDetails, Is.Not.Null);
        Assert.That(problemDetails.Title, Is.EqualTo("Outer exception"));
    }

    #endregion

    #region HandleError Tests

    [Test]
    public void HandleError_Always_ReturnsProblemDetails()
    {
        // Act
        var result = _controller.HandleError();

        // Assert
        Assert.That(result, Is.TypeOf<ObjectResult>());
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult.StatusCode, Is.EqualTo(500));

        var problemDetails = objectResult.Value as ProblemDetails;
        Assert.That(problemDetails, Is.Not.Null);
        Assert.That(problemDetails.Status, Is.EqualTo(500));
        // The default Problem() method doesn't set Title automatically, so test what it actually returns
        Assert.That(problemDetails.Title, Is.Null);
        Assert.That(problemDetails.Type, Is.Null);
    }

    [Test]
    public void HandleError_WhenCalled_DoesNotExposeInternalDetails()
    {
        // Act
        var result = _controller.HandleError();

        // Assert
        var objectResult = result as ObjectResult;
        var problemDetails = objectResult?.Value as ProblemDetails;
        
        Assert.That(problemDetails, Is.Not.Null);
        Assert.That(problemDetails.Detail, Is.Null); // Should not expose stack trace in production
        // The default Problem() method doesn't set Type automatically in our test environment
        Assert.That(problemDetails.Type, Is.Null);
    }

    [Test]
    public void HandleError_WhenCalledMultipleTimes_ReturnsSameResult()
    {
        // Act
        var result1 = _controller.HandleError();
        var result2 = _controller.HandleError();

        // Assert
        Assert.That(result1, Is.TypeOf<ObjectResult>());
        Assert.That(result2, Is.TypeOf<ObjectResult>());
        
        var objectResult1 = result1 as ObjectResult;
        var objectResult2 = result2 as ObjectResult;
        
        Assert.That(objectResult1?.StatusCode, Is.EqualTo(objectResult2?.StatusCode));
        
        var problemDetails1 = objectResult1?.Value as ProblemDetails;
        var problemDetails2 = objectResult2?.Value as ProblemDetails;
        
        Assert.That(problemDetails1?.Status, Is.EqualTo(problemDetails2?.Status));
        Assert.That(problemDetails1?.Title, Is.EqualTo(problemDetails2?.Title));
    }

    #endregion

    #region Constructor Tests

    [Test]
    public void Constructor_WhenCalled_CreatesInstance()
    {
        // Act
        var controller = new ErrorController();

        // Assert
        Assert.That(controller, Is.Not.Null);
        Assert.That(controller, Is.TypeOf<ErrorController>());
    }

    #endregion

    #region Route Tests

    [Test]
    public void ErrorController_HasApiExplorerSettingsIgnoreApi()
    {
        // Arrange & Act
        var controllerType = typeof(ErrorController);
        var attributes = controllerType.GetCustomAttributes(typeof(ApiExplorerSettingsAttribute), false);

        // Assert
        Assert.That(attributes, Has.Length.EqualTo(1));
        var apiExplorerSettings = attributes[0] as ApiExplorerSettingsAttribute;
        Assert.That(apiExplorerSettings, Is.Not.Null);
        Assert.That(apiExplorerSettings.IgnoreApi, Is.True);
    }

    [Test]
    public void HandleErrorDevelopment_HasCorrectRoute()
    {
        // Arrange & Act
        var methodInfo = typeof(ErrorController).GetMethod(nameof(ErrorController.HandleErrorDevelopment));
        var routeAttributes = methodInfo?.GetCustomAttributes(typeof(RouteAttribute), false);

        // Assert
        Assert.That(routeAttributes, Is.Not.Null);
        Assert.That(routeAttributes, Has.Length.EqualTo(1));
        var routeAttribute = routeAttributes[0] as RouteAttribute;
        Assert.That(routeAttribute, Is.Not.Null);
        Assert.That(routeAttribute.Template, Is.EqualTo("/error-development"));
    }

    [Test]
    public void HandleError_HasCorrectRoute()
    {
        // Arrange & Act
        var methodInfo = typeof(ErrorController).GetMethod(nameof(ErrorController.HandleError));
        var routeAttributes = methodInfo?.GetCustomAttributes(typeof(RouteAttribute), false);

        // Assert
        Assert.That(routeAttributes, Is.Not.Null);
        Assert.That(routeAttributes, Has.Length.EqualTo(1));
        var routeAttribute = routeAttributes[0] as RouteAttribute;
        Assert.That(routeAttribute, Is.Not.Null);
        Assert.That(routeAttribute.Template, Is.EqualTo("/error"));
    }

    #endregion
}
