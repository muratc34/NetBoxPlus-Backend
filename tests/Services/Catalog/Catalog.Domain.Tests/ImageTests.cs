using Catalog.Domain.Images;

namespace Catalog.Domain.Tests;

public sealed class ImageTests
{
    [Theory]
    [InlineData(ImageUse.Poster, 300, 450)]
    [InlineData(ImageUse.Poster, 1000, 1500)]
    [InlineData(ImageUse.Backdrop, 1280, 720)]
    [InlineData(ImageUse.Backdrop, 3840, 2160)]
    [InlineData(ImageUse.Photo, 200, 200)]
    [InlineData(ImageUse.Photo, 200, 900)]
    public void Check_AcceptsImagesOfTheRightSizeAndShape(ImageUse use, int width, int height)
        => Assert.True(ImageRules.Check(use, width, height).IsSuccess);

    [Theory]
    [InlineData(ImageUse.Poster, 299, 450, "Image.TooSmall")]
    [InlineData(ImageUse.Poster, 300, 449, "Image.TooSmall")]
    [InlineData(ImageUse.Backdrop, 1279, 720, "Image.TooSmall")]
    [InlineData(ImageUse.Photo, 199, 200, "Image.TooSmall")]
    [InlineData(ImageUse.Poster, 900, 900, "Image.WrongAspectRatio")]
    [InlineData(ImageUse.Backdrop, 1500, 1500, "Image.WrongAspectRatio")]
    [InlineData(ImageUse.Poster, ImageRules.MaxPixels + 1, 12000, "Image.TooLarge")]
    [InlineData(ImageUse.Photo, 400, ImageRules.MaxPixels + 1, "Image.TooLarge")]
    public void Check_RefusesTheRest(ImageUse use, int width, int height, string code)
        => Assert.Equal(code, ImageRules.Check(use, width, height).Error.Code);

    [Fact]
    public void Upload_IsUsableUntilItExpires()
    {
        var file = ImageFile.StartUpload(TestContent.Now);

        Assert.True(file.CheckUsable(file.ExpiresAt.AddSeconds(-1)).IsSuccess);
        Assert.Equal(ImageErrors.UploadExpired, file.CheckUsable(file.ExpiresAt).Error);
    }

    [Fact]
    public void Upload_WorksOnce()
    {
        var file = ImageFile.StartUpload(TestContent.Now);

        file.Activate("a.jpg");

        Assert.Equal(ImageErrors.UploadAlreadyUsed, file.CheckUsable(TestContent.Now).Error);
        Assert.Equal(ImageFileStatus.Active, file.Status);
    }

    [Fact]
    public void Retire_RecordsWhen()
    {
        var file = ImageFile.StartUpload(TestContent.Now);
        file.Activate("a.jpg");

        file.Retire(TestContent.Now);

        Assert.Equal(ImageFileStatus.Retired, file.Status);
        Assert.Equal(TestContent.Now, file.RetiredAt);
    }
}
