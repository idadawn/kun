using Xunit;

namespace Kun.Dapper.Tests;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(1, 20, 100, 5)]
    [InlineData(1, 10, 25, 3)]
    [InlineData(1, 10, 10, 1)]
    public void TotalPages_CalculatesCorrectly(int page, int pageSize, int total, int expected)
    {
        var result = new PagedResult<int>
        {
            Request = new PageRequest(page, pageSize),
            TotalCount = total,
        };
        Assert.Equal(expected, result.TotalPages);
    }

    [Fact]
    public void TotalPages_WhenEmpty_ReturnsZero()
    {
        var result = PagedResult<int>.Empty(new PageRequest());
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public void HasNextPage_WhenOnLastPage_ReturnsFalse()
    {
        var result = new PagedResult<int>
        {
            Request = new PageRequest(Page: 5, PageSize: 20),
            TotalCount = 100,
        };
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void HasNextPage_WhenNotOnLastPage_ReturnsTrue()
    {
        var result = new PagedResult<int>
        {
            Request = new PageRequest(Page: 1, PageSize: 20),
            TotalCount = 100,
        };
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void HasPreviousPage_OnFirstPage_ReturnsFalse()
    {
        var result = new PagedResult<int>
        {
            Request = new PageRequest(Page: 1),
            TotalCount = 100,
        };
        Assert.False(result.HasPreviousPage);
    }

    [Theory]
    [InlineData(1, 20, 0)]
    [InlineData(2, 20, 20)]
    [InlineData(3, 10, 20)]
    public void Offset_CalculatesCorrectly(int page, int pageSize, int expectedOffset)
    {
        var req = new PageRequest(page, pageSize);
        Assert.Equal(expectedOffset, req.Offset);
    }
}
