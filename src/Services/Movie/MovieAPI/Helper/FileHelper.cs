﻿using Shared.Results;
using IResult = Shared.Results.IResult;

namespace MovieAPI.Helper
{
    public class FileHelper
    {
        public static string directory = Environment.CurrentDirectory + @"\wwwroot";
        //public static string path = @"/images/";
        public static string Add(IFormFile file, string path)
        {
            var sourcepath = Path.GetTempFileName();
            if (file.Length > 0)
            {
                using (var stream = new FileStream(sourcepath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }
            }
            var extension = Path.GetExtension(file.FileName);
            var newFileName = Guid.NewGuid().ToString("N") + extension;

            File.Move(sourcepath, directory + path + newFileName);
            return (path + newFileName).Replace("\\", " / ");
        }
        public static IResult Delete(string oldPath, string path)
        {
            path = (directory + oldPath).Replace("/", "\\");
            try
            {
                File.Delete(path);
            }
            catch (Exception exception)
            {
                return new ErrorResult(exception.Message);
            }

            return new SuccessResult();
        }
        public static string Update(string sourcePath, IFormFile file, string path)
        {
            var extension = Path.GetExtension(file.FileName);
            var newFileName = Guid.NewGuid().ToString("N") + extension;

            if (sourcePath.Length > 0)
            {
                using (var stream = new FileStream(directory + path + newFileName, FileMode.Create))
                {
                    file.CopyTo(stream);
                }
            }
            File.Delete(directory + sourcePath);
            return (path + newFileName).Replace("\\", "/");
        }
    }
}
