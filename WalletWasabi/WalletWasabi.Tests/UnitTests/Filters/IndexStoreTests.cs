using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Models;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Helpers;
using WalletWasabi.Stores;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Filters
{
	public class IndexStoreTests
	{
		[Fact]
		public async Task IndexStoreTestsAsync()
		{
			var network = Network.Main;
			var indexStore = new IndexStore(network, new SmartHeaderChain());

			var dir = (await GetIndexStorePathsAsync()).dir;
			if (Directory.Exists(dir))
			{
				Directory.Delete(dir, true);
			}
			await indexStore.InitializeAsync(dir);
		}

		[Fact]
		public async Task InconsistentMatureIndexAsync()
		{
			var (dir, matureFilters, _) = await GetIndexStorePathsAsync();

			var network = Network.Main;
			var headersChain = new SmartHeaderChain();

			var indexStore = new IndexStore(network, headersChain);
			var dummyFilter = GolombRiceFilter.Parse("00");

			static DateTimeOffset MinutesAgo(int mins) => DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(mins));
			var matureIndexStoreContent = new[]
			{
				new FilterModel(new SmartHeader(new uint256(2), new uint256(1), 1, MinutesAgo(30)), dummyFilter),
				new FilterModel(new SmartHeader(new uint256(3), new uint256(2), 2, MinutesAgo(20)), dummyFilter),
				new FilterModel(new SmartHeader(new uint256(99), new uint256(98), 98, MinutesAgo(10)), dummyFilter)
			};
			await File.WriteAllLinesAsync(matureFilters, matureIndexStoreContent.Select(x => x.ToLine()));

			await Assert.ThrowsAsync<InvalidOperationException>(async () => await indexStore.InitializeAsync(dir));
			Assert.Equal(new uint256(3), headersChain.TipHash);
			Assert.Equal(2u, headersChain.TipHeight);

			// Check if the matureIndex is deleted
			Assert.False(File.Exists(matureFilters));
		}

		[Fact]
		public async Task InconsistentImmatureIndexAsync()
		{
			var (dir, _, immatureFilters) = await GetIndexStorePathsAsync();

			var network = Network.Main;
			var headersChain = new SmartHeaderChain();
			var indexStore = new IndexStore(network, headersChain);

			var dummyFilter = GolombRiceFilter.Parse("00");

			static DateTimeOffset MinutesAgo(int mins) => DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(mins));
			var startingFilter = StartingFilters.GetStartingFilter(network);

			var immatureIndexStoreContent = new[]
			{
				new FilterModel(new SmartHeader(new uint256(2), startingFilter.Header.BlockHash, startingFilter.Header.Height + 1, MinutesAgo(30)), dummyFilter),
				new FilterModel(new SmartHeader(new uint256(3), new uint256(2), startingFilter.Header.Height + 2, MinutesAgo(20)), dummyFilter),
				new FilterModel(new SmartHeader(new uint256(99), new uint256(98), startingFilter.Header.Height + 98, MinutesAgo(10)), dummyFilter)
			};
			await File.WriteAllLinesAsync(immatureFilters, immatureIndexStoreContent.Select(x => x.ToLine()));

			await Assert.ThrowsAsync<InvalidOperationException>(async () => await indexStore.InitializeAsync(dir));
			Assert.Equal(new uint256(3), headersChain.TipHash);
			Assert.Equal(startingFilter.Header.Height + 2u, headersChain.TipHeight);

			// Check if the immatureIndex is deleted
			Assert.False(File.Exists(immatureFilters));
		}

		[Fact]
		public async Task GapInIndexAsync()
		{
			var (dir, matureFilters, immatureFilters) = await GetIndexStorePathsAsync();

			var network = Network.Main;
			var headersChain = new SmartHeaderChain();
			var indexStore = new IndexStore(network, headersChain);

			var dummyFilter = GolombRiceFilter.Parse("00");

			static DateTimeOffset MinutesAgo(int mins) => DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(mins));
			var matureIndexStoreContent = new[]
			{
				new FilterModel(new SmartHeader(new uint256(2), new uint256(1), 1, MinutesAgo(30)), dummyFilter),
				new FilterModel(new SmartHeader(new uint256(3), new uint256(2), 2, MinutesAgo(20)), dummyFilter),
			};
			await File.WriteAllLinesAsync(matureFilters, matureIndexStoreContent.Select(x => x.ToLine()));
			var immatureIndexStoreContent = new[]
			{
				new FilterModel(new SmartHeader(new uint256(5), new uint256(4), 4, MinutesAgo(30)), dummyFilter),
				new FilterModel(new SmartHeader(new uint256(6), new uint256(5), 5, MinutesAgo(20)), dummyFilter),
			};
			await File.WriteAllLinesAsync(immatureFilters, immatureIndexStoreContent.Select(x => x.ToLine()));

			await Assert.ThrowsAsync<InvalidOperationException>(async () => await indexStore.InitializeAsync(dir));
			Assert.Equal(new uint256(3), headersChain.TipHash);
			Assert.Equal(2u, headersChain.TipHeight);

			Assert.True(File.Exists(matureFilters));    // mature filters are ok
			Assert.False(File.Exists(immatureFilters)); // immature filters are NOT ok
		}

		[Fact]
		public async Task ReceiveNonMatchingFilterAsync()
		{
			var (dir, matureFilters, immatureFilters) = await GetIndexStorePathsAsync();

			var network = Network.Main;
			var headersChain = new SmartHeaderChain();
			var indexStore = new IndexStore(network, headersChain);

			var dummyFilter = GolombRiceFilter.Parse("00");

			static DateTimeOffset MinutesAgo(int mins) => DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(mins));
			var matureIndexStoreContent = new[]
			{
				new FilterModel(new SmartHeader(new uint256(2), new uint256(1), 1, MinutesAgo(30)), dummyFilter),
				new FilterModel(new SmartHeader(new uint256(3), new uint256(2), 2, MinutesAgo(20)), dummyFilter),
			};
			await File.WriteAllLinesAsync(matureFilters, matureIndexStoreContent.Select(x => x.ToLine()));

			await indexStore.InitializeAsync(dir);
			Assert.Equal(new uint256(3), headersChain.TipHash);
			Assert.Equal(2u, headersChain.TipHeight);

			Assert.True(File.Exists(matureFilters)); // mature filters are ok

			var nonMatchingBlockHashFilter = new FilterModel(new SmartHeader(new uint256(2), new uint256(1), 1, MinutesAgo(30)), dummyFilter);
			await indexStore.AddNewFiltersAsync(new[] { nonMatchingBlockHashFilter }, CancellationToken.None);
			Assert.Equal(new uint256(3), headersChain.TipHash); // the filter is not added!
			Assert.Equal(2u, headersChain.TipHeight);

			var nonMatchingHeightFilter = new FilterModel(new SmartHeader(new uint256(4), new uint256(3), 37, MinutesAgo(1)), dummyFilter);
			await indexStore.AddNewFiltersAsync(new[] { nonMatchingHeightFilter }, CancellationToken.None);
			Assert.Equal(new uint256(3), headersChain.TipHash); // the filter is not added!
			Assert.Equal(2u, headersChain.TipHeight);

			var correctFilter = new FilterModel(new SmartHeader(new uint256(4), new uint256(3), 3, MinutesAgo(1)), dummyFilter);
			await indexStore.AddNewFiltersAsync(new[] { correctFilter }, CancellationToken.None);
			Assert.Equal(new uint256(4), headersChain.TipHash); // the filter is not added!
			Assert.Equal(3u, headersChain.TipHeight);
		}

		private async Task<(string dir, string matureFilters, string immatureFilters)> GetIndexStorePathsAsync([CallerFilePath]string callerFilePath = null, [CallerMemberName] string callerName = "")
		{
			var dir = Path.Combine(Global.Instance.DataDir, EnvironmentHelpers.ExtractFileName(callerFilePath), callerName, "IndexStore");
			await IoHelpers.DeleteRecursivelyWithMagicDustAsync(dir);
			var matureFilters = Path.Combine(dir, "MatureIndex.dat");
			var immatureFilters = Path.Combine(dir, "ImmatureIndex.dat");
			IoHelpers.EnsureContainingDirectoryExists(matureFilters);
			IoHelpers.EnsureContainingDirectoryExists(immatureFilters);
			return (dir, matureFilters, immatureFilters);
		}
	}
}
