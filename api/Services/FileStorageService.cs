namespace api.Services;

public interface IFileStorageService
{
    Task<string> SaveDocumentAsync(IFormFile file, string folder);
    Task<byte[]> GetDocumentAsync(string path);
    Task DeleteDocumentAsync(string path);
    bool ValidateDocument(IFormFile file);
}

public class FileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FileStorageService> _logger;
    private const long MaxFileSize = 10 * 1024 * 1024; // 10MB
    private readonly string[] AllowedExtensions = { ".pdf", ".jpg", ".jpeg", ".png" };
    private readonly string[] AllowedMimeTypes = { 
        "application/pdf", 
        "image/jpeg", 
        "image/jpg", 
        "image/png" 
    };

    public FileStorageService(IWebHostEnvironment environment, ILogger<FileStorageService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public bool ValidateDocument(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return false;

        if (file.Length > MaxFileSize)
        {
            _logger.LogWarning($"File {file.FileName} exceeds max size of 10MB");
            return false;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            _logger.LogWarning($"File {file.FileName} has invalid extension: {extension}");
            return false;
        }

        if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
        {
            _logger.LogWarning($"File {file.FileName} has invalid MIME type: {file.ContentType}");
            return false;
        }

        return true;
    }

    public async Task<string> SaveDocumentAsync(IFormFile file, string folder)
    {
        // Create secure directory outside wwwroot
        var uploadsPath = Path.Combine(_environment.ContentRootPath, "SecureUploads", folder);
        Directory.CreateDirectory(uploadsPath);

        // Generate unique filename
        var uniqueFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(uploadsPath, uniqueFileName);

        // Save file
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        _logger.LogInformation($"Document saved: {filePath}");
        
        // Return relative path for database storage
        return Path.Combine(folder, uniqueFileName);
    }

    public async Task<byte[]> GetDocumentAsync(string relativePath)
    {
        var fullPath = Path.Combine(_environment.ContentRootPath, "SecureUploads", relativePath);
        
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Document not found");
        }

        return await File.ReadAllBytesAsync(fullPath);
    }

    public async Task DeleteDocumentAsync(string relativePath)
    {
        var fullPath = Path.Combine(_environment.ContentRootPath, "SecureUploads", relativePath);
        
        if (File.Exists(fullPath))
        {
            await Task.Run(() => File.Delete(fullPath));
            _logger.LogInformation($"Document deleted: {fullPath}");
        }
    }
}

