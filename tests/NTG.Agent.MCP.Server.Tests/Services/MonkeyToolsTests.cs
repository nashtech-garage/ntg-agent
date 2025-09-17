
using NTG.Agent.MCP.Server.McpTools;
using NTG.Agent.MCP.Server.Services;
using System.Text.Json;

namespace NTG.Agent.MCP.Server.Tests.Services;

[TestFixture]
public class MonkeyToolsTests
{
	private TestMonkeyService _testMonkeyService = null!;
	private MonkeyTools _monkeyTools = null!;

	[SetUp]
	public void SetUp()
	{
		_testMonkeyService = new TestMonkeyService();
		_monkeyTools = new MonkeyTools(_testMonkeyService);
	}

	class TestMonkeyService : MonkeyService
	{
		public List<Monkey>? MonkeysToReturn { get; set; }
		public Monkey? MonkeyToReturn { get; set; }
		public string? LastGetMonkeyName { get; set; }

		public TestMonkeyService() : base(new FakeHttpClientFactory()) { }

		public override Task<List<Monkey>> GetMonkeys()
		{
			return Task.FromResult(MonkeysToReturn ?? new List<Monkey>());
		}

		public override Task<Monkey?> GetMonkey(string name)
		{
			LastGetMonkeyName = name;
			return Task.FromResult(MonkeyToReturn);
		}
	}

	class FakeHttpClientFactory : System.Net.Http.IHttpClientFactory
	{
		public System.Net.Http.HttpClient CreateClient(string name = null!) => new System.Net.Http.HttpClient();
	}

	[Test]
	public async Task GetMonkeys_ReturnsSerializedList()
	{
		// Arrange
		var monkeys = new List<Monkey>
		{
			new Monkey { Name = "George", Location = "Jungle", Details = "Curious", Image = "george.jpg", Population = 1, Latitude = 1.1, Longitude = 2.2 },
			new Monkey { Name = "Abu", Location = "Desert", Details = "Agile", Image = "abu.jpg", Population = 2, Latitude = 3.3, Longitude = 4.4 }
		};
		_testMonkeyService.MonkeysToReturn = monkeys;

		// Act
		var result = await _monkeyTools.GetMonkeys();

		// Assert
		Assert.That(result, Is.EqualTo(JsonSerializer.Serialize(monkeys)));
	}

	[Test]
	public async Task GetMonkey_ReturnsSerializedMonkey()
	{
		// Arrange
		var monkey = new Monkey { Name = "George", Location = "Jungle", Details = "Curious", Image = "george.jpg", Population = 1, Latitude = 1.1, Longitude = 2.2 };
		_testMonkeyService.MonkeyToReturn = monkey;

		// Act
		var result = await _monkeyTools.GetMonkey("George");

		// Assert
		Assert.That(result, Is.EqualTo(JsonSerializer.Serialize(monkey)));
		Assert.That(_testMonkeyService.LastGetMonkeyName, Is.EqualTo("George"));
	}

	[Test]
	public void Constructor_NullService_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => new MonkeyTools(null!));
	}

	[Test]
	public async Task GetMonkey_WhenServiceReturnsNull_ReturnsNullJson()
	{
		// Arrange
		_testMonkeyService.MonkeyToReturn = null;

		// Act
		var result = await _monkeyTools.GetMonkey("Unknown");

		// Assert
		Assert.That(result, Is.EqualTo("null"));
	}

	[Test]
	public async Task GetMonkeys_WhenServiceReturnsEmpty_ReturnsEmptyArrayJson()
	{
		// Arrange
		_testMonkeyService.MonkeysToReturn = new List<Monkey>();

		// Act
		var result = await _monkeyTools.GetMonkeys();

		// Assert
		Assert.That(result, Is.EqualTo(JsonSerializer.Serialize(_testMonkeyService.MonkeysToReturn)));
	}
}
