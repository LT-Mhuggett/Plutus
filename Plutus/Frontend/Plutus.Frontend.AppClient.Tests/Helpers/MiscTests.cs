using Microsoft.Maui.Graphics;
using Plutus.Frontend.AppClient.Exceptions;
using Plutus.Frontend.AppClient.Helpers.EventArgs;
using Plutus.Frontend.AppClient.Helpers.Extensions.XAML;
using Plutus.Frontend.AppClient.Services.Loading;

namespace Plutus.Frontend.AppClient.Tests.Helpers
{
    public class ConflictExceptionTests
    {
        [Fact]
        public void Constructor_SetsMessage()
        {
            var ex = new ConflictException("conflict");
            Assert.Equal("conflict", ex.Message);
        }
    }

    public class LoadingViewServiceTests
    {
        [Fact]
        public void InitLoadingPage_WithNonLoadingIndicatorViewPage_DoesNotThrow()
        {
            // ShowLoadingPage/HideLoadingPage need a live App/Application.Current, which can't be
            // constructed here (see AppViewModelTests), but InitLoadingPage itself is a safe `as` cast
            // that accepts (and discards) any ContentPage - null included - without touching the wall.
            var service = new LoadingViewService();
            var ex = Record.Exception(() => service.InitLoadingPage(null!));
            Assert.Null(ex);
        }
    }

    public class FolderPickerNotInitalizedExceptionTests
    {
        [Fact]
        public void DefaultConstructor_HasNoMessage()
        {
            var ex = new FolderPickerNotInitalizedException();
            Assert.NotNull(ex.Message);
        }

        [Fact]
        public void MessageConstructor_SetsMessage()
        {
            var ex = new FolderPickerNotInitalizedException("boom");
            Assert.Equal("boom", ex.Message);
        }

        [Fact]
        public void MessageAndInnerExceptionConstructor_SetsBoth()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new FolderPickerNotInitalizedException("boom", inner);
            Assert.Equal("boom", ex.Message);
            Assert.Same(inner, ex.InnerException);
        }
    }

    public class ItemRightTappedEventArgsTests
    {
        [Fact]
        public void Constructor_SetsItemDataAndPosition()
        {
            var position = new Point(1, 2);
            var args = new ItemRightTappedEventArgs("item", position);

            Assert.Equal("item", args.ItemData);
            Assert.Equal(position, args.Position);
        }
    }

    public class ValueChangedEventArgsTests
    {
        [Fact]
        public void Constructor_SetsOldAndNewValue()
        {
            var args = new ValueChangedEventArgs<int>(1, 2);

            Assert.Equal(1, args.OldValue);
            Assert.Equal(2, args.NewValue);
        }
    }

    public class ByteArrayToImageSourceConverterTests
    {
        [Fact]
        public void Convert_Null_ReturnsNull()
        {
            var converter = new ByteArrayToImageSourceConverter();
            Assert.Null(converter.Convert(null!, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Convert_NonByteArray_ThrowsArgumentException()
        {
            var converter = new ByteArrayToImageSourceConverter();
            Assert.Throws<ArgumentException>(() =>
                converter.Convert("not bytes", typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
        }

        [Fact]
        public void ConvertBack_AlwaysThrowsNotSupported()
        {
            var converter = new ByteArrayToImageSourceConverter();
            Assert.Throws<NotSupportedException>(() =>
                converter.ConvertBack(null!, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
