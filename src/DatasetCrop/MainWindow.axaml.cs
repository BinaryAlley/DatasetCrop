#region ========================================================================= USING =====================================================================================
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DatasetCrop.MVVM;
using MessageBox.Avalonia;
using MessageBox.Avalonia.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
#endregion

namespace DatasetCrop;

/// <summary>
/// Code behind for the application's main window
/// </summary>
/// <remarks>
/// Creation Date: 05th of January, 2024
/// </remarks>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    #region ================================================================== FIELD MEMBERS ================================================================================
    private bool isDragStarted = false;
    public new event PropertyChangedEventHandler? PropertyChanged;
    public static event Action<int>? ThemeChanged;
    #endregion

    #region ================================================================= BINDING COMMANDS ==============================================================================
    public IAsyncCommand CropImagesAsync_Command { get; private set; }
    public IAsyncCommand BrowseInputAsync_Command { get; private set; }
    public IAsyncCommand BrowseOutputAsync_Command { get; private set; }
    public IAsyncCommand RefreshInputAsync_Command { get; private set; }
    #endregion

    #region ================================================================ BINDING PROPERTIES =============================================================================
    private int cropWidth = 50;
    public int CropWidth
    {
        get { return cropWidth; }
        set { cropWidth = value; Notify(); }
    }

    private int cropHeight = 50;
    public int CropHeight
    {
        get { return cropHeight; }
        set { cropHeight = value; Notify(); }
    }

    private int cropX = 0;
    public int CropX
    {
        get { return cropX; }
        set { cropX = value; Notify(); }
    }

    private int cropY = 0;
    public int CropY
    {
        get { return cropY; }
        set { cropY = value; Notify(); }
    }

    private int previewWidth = 100;
    public int PreviewWidth
    {
        get { return previewWidth; }
        set { previewWidth = value; Notify(); }
    }

    private int previewHeight = 100;
    public int PreviewHeight
    {
        get { return previewHeight; }
        set { previewHeight = value; Notify(); }
    }

    private string? inputPath;
    public string? InputPath
    {
        get { return inputPath; }
        set { inputPath = value; Notify(); }
    }

    private string? outputPath;
    public string? OutputPath
    {
        get { return outputPath; }
        set { outputPath = value; Notify(); }
    }
    
    private bool isSelectionMode;
    public bool IsSelectionMode
    {
        get { return isSelectionMode; }
        set { isSelectionMode = value; Notify(); }
    }

    private bool usesOriginalScaleSizes = true;
    public bool UsesOriginalScaleSizes
    {
        get { return usesOriginalScaleSizes; }
        set { usesOriginalScaleSizes = value; Notify(); }
    }
    #endregion

    #region ====================================================================== CTOR =====================================================================================
    /// <summary>
    /// Default C-tor
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        CropImagesAsync_Command = new AsyncCommand(CropImagesAsync);
        RefreshInputAsync_Command = new AsyncCommand(RefreshImagePreviewsAsync);
        BrowseInputAsync_Command = new AsyncCommand(BrowseInputAsync);
        BrowseOutputAsync_Command = new AsyncCommand(BrowseOutputAsync);
        DataContext = this;
        App.StyleManager?.SwitchThemeByIndex(1);
    }
    #endregion

    #region ===================================================================== METHODS ===================================================================================
    /// <summary>
    /// Displays a dialog for browsing the directory where the dataset input images are located
    /// </summary>
    private async Task BrowseInputAsync()
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions() { AllowMultiple = false, Title = "Choose dataset images directory" });
        if (result.Count > 0 && result[0].TryGetUri(out Uri? directory))
            InputPath = directory.LocalPath;
    }

    /// <summary>
    /// Displays a dialog for browsing the directory where the dataset output images will be cropped
    /// </summary>
    private async Task BrowseOutputAsync()
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions() { AllowMultiple = false, Title = "Choose cropped images directory" });
        if (result.Count > 0 && result[0].TryGetUri(out Uri? directory))
            OutputPath = directory.LocalPath;
    }

    /// <summary>
    /// Refreshes the list of detected image in the input folder, and displays them in the dragPanel
    /// </summary>
    private async Task RefreshImagePreviewsAsync()
    {
        // perform validations
        if (!await ValidateInputPathAsync())
            return;
        if (!await ValidateCropParameters())
            return;
        Title = "Dataset Crop - Working...";
        // clear previous image previews
        ClearImagePreviews();
        int column = 0;
        int row = 0;
        int margin = 5;
        await Task.Run(async () => 
        { 
            // iterate all files in the input directory
            var filePaths = Directory.GetFiles(InputPath!, "*.jpg")
                                     .Concat(Directory.GetFiles(InputPath!, "*.jpeg"))
                                     .Concat(Directory.GetFiles(InputPath!, "*.png"))
                                     .Concat(Directory.GetFiles(InputPath!, "*.bmp"));
            var loadTasks = filePaths.Select(async file =>
            {
                using (var tempImage = await SixLabors.ImageSharp.Image.LoadAsync(file))
                {
                    var originalSize = new Avalonia.Size(tempImage.Width, tempImage.Height);
                    var resizedImage = await LoadResizedImageAsync(tempImage, previewWidth, previewHeight);
                    return new { FilePath = file, Image = resizedImage, OriginalSize = originalSize };
                }
            });

            var loadedImages = await Task.WhenAll(loadTasks);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var loadedImage in loadedImages)
                {
                    // for each image file, create a grid that contains an image and a drag panel, and add it to the previews list
                    Grid container = new();
                    container.Width = previewWidth;
                    container.Height = previewHeight;
                    container.Margin = new Thickness(column * (previewWidth + margin), row * (previewHeight + margin), 0, 0);
                    container.HorizontalAlignment = HorizontalAlignment.Left;
                    container.VerticalAlignment = VerticalAlignment.Top;
                    container.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0, 0, 0), 0.1);
                    Avalonia.Controls.Image image = new();
                    image.Width = previewWidth;
                    image.Height = previewHeight;
                    image.Source = loadedImage.Image;
                    image.Margin = new Thickness(0);
                    image.HorizontalAlignment = HorizontalAlignment.Left;
                    image.VerticalAlignment = VerticalAlignment.Top;
                    image.Tag = loadedImage.FilePath; // store the path of the original image file
                    ToolTip.SetTip(image, loadedImage.FilePath);
                    container.Children.Add(image);

                    Panel dragPanel = new();
                    if (usesOriginalScaleSizes)
                    {
                        // calculate the uniform scaling factor based on the largest dimension
                        double scaleFactor = Math.Max(loadedImage.OriginalSize.Width / previewWidth, loadedImage.OriginalSize.Height / previewHeight);
                        // scale down the crop size and position uniformly
                        dragPanel.Width = cropWidth / scaleFactor;
                        dragPanel.Height = cropHeight / scaleFactor;
                        dragPanel.Margin = new Thickness(cropX / scaleFactor, cropY / scaleFactor, 0, 0);
                    }
                    else
                    {
                        dragPanel.Width = cropWidth;
                        dragPanel.Height = cropHeight;
                        dragPanel.Margin = new Thickness(cropX, cropY, 0, 0);
                    }
                    // for each image, ensure the drag panel is not in any way bigger than the visible area of the scaled down image preview
                    if (!ValidateCropPanelSize(loadedImage.OriginalSize.Width, loadedImage.OriginalSize.Height, cropWidth, cropHeight, previewWidth, previewHeight))
                    {
                        dragPanel.Width = previewWidth;
                        dragPanel.Height = previewHeight;
                        dragPanel.Tag = false; // do not select the image, if its crop parameters are not ok 
                        dragPanel.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 0, 0), 0.3);
                        dragPanel.IsEnabled = false; // don't allow dragging, when the image is not valid for cropping
                    }
                    else
                    {
                        dragPanel.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 255, 255), 0.3);
                        dragPanel.Tag = true; // true = "selected" (will be cropped), false = "deselected" (will be ignored when cropping)
                    }
                    dragPanel.HorizontalAlignment = HorizontalAlignment.Left;
                    dragPanel.VerticalAlignment = VerticalAlignment.Top;
                    dragPanel.Cursor = new Cursor(StandardCursorType.SizeAll);
                    dragPanel.PointerMoved += DragPanel_PointerMoved; // subscribe the event handlers used for dragging
                    dragPanel.PointerPressed += DragPanel_PointerPressed;
                    dragPanel.PointerReleased += DragPanel_PointerReleased;

                    container.Children.Add(dragPanel);
                    grdImages.Children.Add(container);

                    // increment column until the remaining horizontal space can no longer fit a whole image preview, then reset it and increment the row
                    if ((column + 2) * (previewWidth + margin) < Width - 12) // 12: 6 pixels for margin on each side for the images dragPanel
                        column++;
                    else
                    {
                        column = 0;
                        row++;
                    }
                }
            });
        });
        Title = "Dataset Crop";
    }

    /// <summary>
    /// Loads an image and returns a scaled down version of it, as Bitmap
    /// </summary>
    /// <param name="image">The image to load</param>
    /// <param name="targetWidth">The width of the scaled down bitmap</param>
    /// <param name="targetHeight">The height of the scaled down bitmap</param>
    /// <returns>A scaled down bitmap of the original image</returns>
    public static async Task<Bitmap> LoadResizedImageAsync(SixLabors.ImageSharp.Image image, int targetWidth, int targetHeight)
    {
        // Calculate scale ratio to maintain aspect ratio
        var scale = Math.Min(targetWidth / (float)image.Width, targetHeight / (float)image.Height);

        image.Mutate(x => x.Resize((int)(image.Width * scale), (int)(image.Height * scale)));
        var memoryStream = new MemoryStream();
        await image.SaveAsBmpAsync(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);
        return new Bitmap(memoryStream);
    }

    /// <summary>
    /// Clears all the image previews from the container
    /// </summary>
    private void ClearImagePreviews()
    {
        // not truly necessary unless special cases, but better safe than sorry
        foreach (var grid in grdImages.Children.OfType<Grid>())
        {
            foreach (var panel in grid.Children.OfType<Panel>())
            {
                panel.PointerMoved -= DragPanel_PointerMoved;
                panel.PointerPressed -= DragPanel_PointerPressed;
                panel.PointerReleased -= DragPanel_PointerReleased;
            }
        }
        grdImages.Children.Clear();
    }

    /// <summary>
    /// Crops the images displayed in the preview dragPanel to the specified parameters
    /// </summary>
    private async Task CropImagesAsync()
    {
        // validation stuff
        if (!await ValidateInputPathAsync())
            return;
        if (!await ValidateOutputPathAsync())
            return;
        if (!await ValidateCropParameters())
            return;
        if (!await ValidateImageFiles())
            return;
        Title = "Dataset Crop - Working...";
        try
        {
            foreach (var container in grdImages.Children.OfType<Grid>())
            {
                var dragPanel = container.Children.OfType<Panel>().FirstOrDefault();
                var imageControl = container.Children.OfType<Avalonia.Controls.Image>().FirstOrDefault();
                if (dragPanel != null && imageControl != null && (bool)dragPanel.Tag!) // only process "selected" images
                {
                    var originalImagePath = imageControl.Tag?.ToString()!;
                    var originalExtension = Path.GetExtension(originalImagePath);
                    var croppedImagePath = Path.Combine((Path.HasExtension(OutputPath) ? Path.GetDirectoryName(OutputPath) : OutputPath)!, Path.GetFileNameWithoutExtension(originalImagePath) + "-cropped" + originalExtension);
                    using (var tempImage = await SixLabors.ImageSharp.Image.LoadAsync(originalImagePath))
                    {
                        // original image dimensions
                        var originalWidth = tempImage.Width;
                        var originalHeight = tempImage.Height;

                        // calculate scaling factor
                        var scaleX = originalWidth / imageControl.Bounds.Width;
                        var scaleY = originalHeight / imageControl.Bounds.Height;
                        // translate crop area to original scale
                        double originalCropX, originalCropY, originalCropWidth, originalCropHeight;
                        if (usesOriginalScaleSizes)
                        {
                            originalCropX = dragPanel.Margin.Left * scaleX;
                            originalCropY = dragPanel.Margin.Top * scaleY;
                            originalCropWidth = cropWidth;
                            originalCropHeight = cropHeight;
                        }
                        else
                        {
                            originalCropX = dragPanel.Margin.Left * scaleX;
                            originalCropY = dragPanel.Margin.Top * scaleY;
                            originalCropWidth = dragPanel.Width * scaleX;
                            originalCropHeight = dragPanel.Height * scaleY;
                        }
                        IImageFormat imageFormat = originalExtension.ToLower() switch
                        {
                            ".png" => PngFormat.Instance,
                            ".bmp" => BmpFormat.Instance,
                            _ => JpegFormat.Instance
                        };
                        // perform the crop operation
                        byte[] croppedImageBytes = await CropImageAsync(tempImage, originalCropX, originalCropY, originalCropWidth, originalCropHeight, imageFormat);
                        // save the cropped image back to disk
                        await File.WriteAllBytesAsync(croppedImagePath, croppedImageBytes);
                    }
                }
            }
            Title = "Dataset Crop";
            await MessageBoxManager.GetMessageBoxStandardWindow("Success!", "Images cropped!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Success).ShowDialog(this);
        }
        catch (Exception ex)
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "There was an error cropping the images!" + Environment.NewLine + ex, ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
        }
    }

    /// <summary>
    /// Crops an image to a specified area
    /// </summary>
    /// <param name="image">The source image</param>
    /// <param name="cropX">The X coordinate of the top-left corner of the crop area</param>
    /// <param name="cropY">The Y coordinate of the top-left corner of the crop area</param>
    /// <param name="cropWidth">The width of the crop area</param>
    /// <param name="cropHeight">The height of the crop area</param>
    /// <param name="imageFormat">The format of the image to save</param>
    /// <returns>The byte array of the cropped image</returns>
    private static async Task<byte[]> CropImageAsync(SixLabors.ImageSharp.Image image, double cropX, double cropY, double cropWidth, double cropHeight, IImageFormat imageFormat)
    {
        //using var inputMemoryStream = new MemoryStream(imageBytes);
        using var outputMemoryStream = new MemoryStream();
        // define the crop area
        var cropRectangle = new Rectangle((int)cropX, (int)cropY, (int)cropWidth, (int)cropHeight);
        // crop the image
        image.Mutate(ctx => ctx.Crop(cropRectangle));
        // save the cropped image
        await image.SaveAsync(outputMemoryStream, imageFormat);
        return outputMemoryStream.ToArray();
    }

    #region Validation
    /// <summary>
    /// Validates the required information for setting the input path
    /// </summary>
    /// <returns><see langword="true"/> if the required information is met, <see langword="false"/> otherwise</returns>
    private async Task<bool> ValidateInputPathAsync()
    {
        if (string.IsNullOrWhiteSpace(InputPath))
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Input path cannot be empty!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!Path.Exists(InputPath))
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Input path does not exist!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Validates the required information for setting the output path
    /// </summary>
    /// <returns><see langword="true"/> if the required information is met, <see langword="false"/> otherwise</returns>
    private async Task<bool> ValidateOutputPathAsync()
    {
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Output path cannot be empty!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!Path.Exists(OutputPath))
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Output path does not exist!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Validates the required information for setting the size of the crop dragPanel
    /// </summary>
    /// <returns><see langword="true"/> if the required information is met, <see langword="false"/> otherwise</returns>
    private bool ValidateCropPanelSize(double originalWidth, double originalHeight, int cropWidth, int cropHeight, int previewWidth, int previewHeight)
    {
        // calculate the aspect ratio of the original image
        double aspectRatio = (double)originalWidth / originalHeight;
        // calculate the scaled dimensions of the image
        double scaledWidth, scaledHeight;
        if (aspectRatio >= 1) // width is greater than or equal to height
        {
            scaledWidth = previewWidth;
            scaledHeight = previewWidth / aspectRatio;
        }
        else // height is greater
        {
            scaledHeight = previewHeight;
            scaledWidth = previewHeight * aspectRatio;
        }
        if (usesOriginalScaleSizes)
            return cropWidth <= originalWidth && cropHeight <= originalHeight;
        else // check if the crop dragPanel exceeds the scaled dimensions        
            return cropWidth <= scaledWidth && cropHeight <= scaledHeight;
    }

    /// <summary>
    /// Validates the required information for setting the crop parameters
    /// </summary>
    /// <returns><see langword="true"/> if the required information is met, <see langword="false"/> otherwise</returns>
    private async Task<bool> ValidateCropParameters()
    {
        if (!ValidateCropHeight())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Crop height must be greater than zero!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!ValidateCropWidth())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Crop width must be greater than zero!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!ValidateCropX())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Crop X must be greater than or equal to zero!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!ValidateCropY())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Crop Y must be greater than or equal to zero!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!ValidateCropRectangleWidthFit())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Crop X plus Crop Width cannot be greater than preview width!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!ValidateCropRectangleHeightFit())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Crop Y plus Crop Height cannot be greater than preview height!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!ValidatePreviewHeight())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Preview height must be greater than zero!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!ValidatePreviewWidth())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Preview width must be greater than zero!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!ValidateCropAndDragRectanglesWidthFit())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Crop width cannot be greater than the dragPanel width!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!ValidateCropAndDragRectanglesHeightFit())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Crop height cannot be greater than the dragPanel height!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!ValidateCropAndPreviewRectanglesWidthFit())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Crop width cannot be greater than preview width!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else if (!ValidateCropAndPreviewRectanglesHeightFit())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Crop height cannot be greater than preview height!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else
            return true;
    }

    /// <summary>
    /// Validates that the crop X coordinate is positive
    /// </summary>
    /// <returns><see langword="true"/> if <see cref="cropX"/> is positive, <see langword="false"/> otherwise</returns>
    private bool ValidateCropX()
    {
        return cropX >= 0;
    }

    /// <summary>
    /// Validates that the crop Y coordinate is positive
    /// </summary>
    /// <returns><see langword="true"/> if <see cref="cropY"/> is positive, <see langword="false"/> otherwise</returns>
    private bool ValidateCropY()
    {
        return cropY >= 0;
    }

    /// <summary>
    /// Validates that the crop width is positive
    /// </summary>
    /// <returns><see langword="true"/> if <see cref="cropWidth"/> is positive, <see langword="false"/> otherwise</returns>
    private bool ValidateCropWidth()
    {
        return cropWidth > 0;
    }

    /// <summary>
    /// Validates that the crop height is positive
    /// </summary>
    /// <returns><see langword="true"/> if <see cref="cropHeight"/> is positive, <see langword="false"/> otherwise</returns>
    private bool ValidateCropHeight()
    {
        return cropHeight > 0;
    }

    /// <summary>
    /// Validates that the preview width is positive
    /// </summary>
    /// <returns><see langword="true"/> if <see cref="previewWidth"/> is positive, <see langword="false"/> otherwise</returns>
    private bool ValidatePreviewWidth()
    {
        return previewWidth > 0;
    }

    /// <summary>
    /// Validates that the preview height is positive
    /// </summary>
    /// <returns><see langword="true"/> if <see cref="previewHeight"/> is positive, <see langword="false"/> otherwise</returns>
    private bool ValidatePreviewHeight()
    {
        return previewHeight > 0;
    }

    /// <summary>
    /// Validates that <see cref="cropWidth"/> plus the <see cref="cropX"/> coordinate are within the image bounds
    /// </summary>
    /// <returns><see langword="true"/> if the <see cref="cropWidth"/> is within image bounds, <see langword="false"/> otherwise</returns>
    private bool ValidateCropRectangleWidthFit()
    {
        return usesOriginalScaleSizes || cropX + cropWidth <= previewWidth;
    }

    /// <summary>
    /// Validates that <see cref="cropHeight"/> plus the <see cref="cropY"/> coordinate are within the image bounds
    /// </summary>
    /// <returns><see langword="true"/> if the <see cref="cropHeight"/> is within image bounds, <see langword="false"/> otherwise</returns>
    private bool ValidateCropRectangleHeightFit()
    {
        return usesOriginalScaleSizes || cropY + cropHeight <= previewHeight;
    }

    /// <summary>
    /// Validates that <see cref="cropWidth"/> is smaller or equal than the drag panel's width
    /// </summary>
    /// <returns><see langword="true"/> if the crop rectangle width is within drag panel's width, <see langword="false"/> otherwise</returns>
    private bool ValidateCropAndDragRectanglesWidthFit()
    {
        return usesOriginalScaleSizes || cropWidth <= grdImages.Width;
    }

    /// <summary>
    /// Validates that <see cref="cropHeight"/> is smaller or equal than the drag panel's height
    /// </summary>
    /// <returns><see langword="true"/> if the crop rectangle height is within drag panel's height, <see langword="false"/> otherwise</returns>
    private bool ValidateCropAndDragRectanglesHeightFit()
    {
        return usesOriginalScaleSizes || cropHeight <= grdImages.Height;
    }

    /// <summary>
    /// Validates that <see cref="cropWidth"/> is smaller or equal than the preview panel's width
    /// </summary>
    /// <returns><see langword="true"/> if the crop rectangle width is within preview panel's width, <see langword="false"/> otherwise</returns>
    private bool ValidateCropAndPreviewRectanglesWidthFit()
    {
        return usesOriginalScaleSizes || cropWidth <= previewWidth;
    }

    /// <summary>
    /// Validates that <see cref="cropHeight"/> is smaller or equal than the preview panel's height
    /// </summary>
    /// <returns><see langword="true"/> if the crop rectangle height is within preview panel's height, <see langword="false"/> otherwise</returns>
    private bool ValidateCropAndPreviewRectanglesHeightFit()
    {
        return usesOriginalScaleSizes || cropHeight <= previewHeight;
    }

    /// <summary>
    /// Validates that the specified input path contains images or not
    /// </summary>
    /// <returns><see langword="true"/> if there are image files at <see cref="InputPath"/>, <see langword="false"/> otherwise</returns>
    private async Task<bool> ValidateImageFiles()
    {
        if (!grdImages.Children.OfType<Grid>().Any())
        {
            await MessageBoxManager.GetMessageBoxStandardWindow("Error!", "Specified directory contains no image files!", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error).ShowDialog(this);
            return false;
        }
        else
            return true;
    }
    #endregion

    /// <summary>
    /// Notifies subscribers about a property's value being changed
    /// </summary>
    /// <param name="propName">The property that had the value changed</param>
    public virtual void Notify([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
    #endregion

    #region ================================================================== EVENT HANDLERS ===============================================================================
    /// <summary>
    /// Event handler for the program quit menu item
    /// </summary>
    private async void CloseApplication_Click(object sender, RoutedEventArgs e)
    {
        if (await MessageBoxManager.GetMessageBoxStandardWindow("Confirmation", "Are you sure you want to close this program?", 
            ButtonEnum.YesNo, MessageBox.Avalonia.Enums.Icon.Question).ShowDialog(this) == ButtonResult.Yes)
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopApp)
                desktopApp.Shutdown();
    }
    
    /// <summary>
    /// Event handler for the light theme menu item
    /// </summary>
    private void SetLightTheme_Click(object sender, RoutedEventArgs e)
    {
        ThemeChanged?.Invoke(1);
    }
    
    /// <summary>
    /// Event handler for the dark theme menu item
    /// </summary>
    private void SetDarkTheme_Click(object sender, RoutedEventArgs e)
    {
        ThemeChanged?.Invoke(0);
    }
    
    /// <summary>
    /// Handles Drag Panel's PointerReleased event
    /// </summary>
    private void DragPanel_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        isDragStarted = false;
    }

    /// <summary>
    /// Handles Drag Panel's PointerPressed event
    /// </summary>
    private void DragPanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsSelectionMode)
        {
            if (sender is Panel dragPanel) 
            {
                bool isSelected = (bool)dragPanel.Tag!;
                dragPanel.Tag = !isSelected;
                dragPanel.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(255, (byte)(!isSelected ? 255 : 0), (byte)(!isSelected ? 255 : 0)), 0.3); 
            }
        }
        else // not in selection mode, just enable dragging
            isDragStarted = true;
    }

    /// <summary>
    /// Handles Drag Panel's PointerMoved event
    /// </summary>
    private void DragPanel_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (isDragStarted && sender is Panel panel && panel.Parent is Grid container)
        {
            // get the current mouse position relative to the parent grid
            var position = e.GetPosition(container);
            // calculate new position
            var newX = position.X - panel.Width / 2;
            var newY = position.Y - panel.Height / 2;
            // get the image control inside the container
            var image = container.Children.OfType<Avalonia.Controls.Image>().FirstOrDefault();
            if (image != null)
            {
                var imageBounds = new Rect(0, 0, image.Bounds.Width, image.Bounds.Height);
                // constrain within image bounds
                newX = Math.Max(imageBounds.Left, Math.Min(imageBounds.Right - panel.Width, newX));
                newY = Math.Max(imageBounds.Top, Math.Min(imageBounds.Bottom - panel.Height, newY));
            }
            // update the position of the dragPanel
            panel.Margin = new Thickness(newX, newY, 0, 0);
        }
    }
    
    /// <summary>
    /// Handles the window key down events.
    /// </summary>
    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (IsSelectionMode)
        {
            foreach (var container in grdImages.Children.OfType<Grid>())
            {
                var dragPanel = container.Children.OfType<Panel>().FirstOrDefault();
                if (!dragPanel.IsEnabled) // skip images where the drag panel is bigger than the images - they are not valid for selecting/cropping
                    continue;
                if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.A) // ctrl+a selects all
                {
                    dragPanel.Tag = true;
                    dragPanel.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 255, 255), 0.3); 
                }
                else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.N) // ctrl+n selects none
                {
                    dragPanel.Tag = false;
                    dragPanel.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 0, 0), 0.3); 
                }
                else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.I) // ctrl+i inverts selection
                {
                    bool isSelected = (bool)dragPanel.Tag!;
                    dragPanel.Tag = !isSelected;
                    dragPanel.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(255, (byte)(!isSelected ? 255 : 0), (byte)(!isSelected ? 255 : 0)), 0.3); 
                }
            }
        }
    }

    /// <summary>
    /// Handles Show Key Bindings click event. 
    /// </summary>
    private async void ShowKeyBingings_Click(object? sender, RoutedEventArgs e)
    {
        await MessageBoxManager.GetMessageBoxStandardWindow("Key Bindings", 
            "Ctrl + A: in Selection Mode, selects all images\n" +
            "Ctrl + N: in Selection Mode, deselects all images\n" +
            "Ctrl + I: in Selection Mode, inverts images selection\n", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Info).ShowDialog(this);
    }
    #endregion
} 