﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Hosting;
using Tinifier.Core.Infrastructure;
using Tinifier.Core.Models;
using Tinifier.Core.Models.API;
using Tinifier.Core.Models.Db;
using Tinifier.Core.Repository.Image;
using Tinifier.Core.Services.BackendDevs;
using Tinifier.Core.Services.History;
using Tinifier.Core.Services.Media.Organizers;
using Tinifier.Core.Services.Settings;
using Tinifier.Core.Services.State;
using Tinifier.Core.Services.Statistic;
using Tinifier.Core.Services.TinyPNG;
using Tinifier.Core.Services.Validation;
using Umbraco.Core.IO;
using Tinifier.Core.Infrastructure.Exceptions;
using Tinifier.Core.Repository.History;
using Umbraco.Core.Services;
using uMedia = Umbraco.Core.Models.Media;

namespace Tinifier.Core.Services.Media
{
    public class ImageService : BaseImageService, IImageService, IMediaHistoryService
    {
        private readonly IValidationService _validationService;
        private readonly TImageRepository _imageRepository;
        private readonly TMediaHistoryRepository _mediaHistoryRepository;
        private readonly IHistoryService _historyService;
        private readonly IStatisticService _statisticService;
        private readonly IStateService _stateService;
        private readonly ITinyPNGConnector _tinyPngConnectorService;
        private readonly IBackendDevsConnector _backendDevsConnectorService;
        private readonly IMediaService _mediaService;
        private readonly ISettingsService _settingsService;
        private readonly IImageHistoryService _imageHistoryService;

        public ImageService()
        {
            _imageRepository = new TImageRepository();
            _mediaHistoryRepository = new TMediaHistoryRepository();
            _validationService = new ValidationService();
            _historyService = new HistoryService();
            _statisticService = new StatisticService();
            _stateService = new StateService();
            _tinyPngConnectorService = new TinyPNGConnectorService();
            _backendDevsConnectorService = new BackendDevsConnectorService();
            _mediaService = Umbraco.Core.ApplicationContext.Current.Services.MediaService;
            _settingsService = new SettingsService();
            _imageHistoryService = new TImageHistoryService();
        }

        public IEnumerable<TImage> GetAllImages()
        {
            return Convert(_imageRepository.GetAll());
        }

        public IEnumerable<uMedia> GetAllImagesAt(int folderId)
        {
            return _imageRepository.GetAllAt(folderId);
        }

        public TImage GetImage(int id)
        {
            return GetImage(_imageRepository.Get(id));
        }

        public TImage GetImage(string path)
        {
            return GetImage(_imageRepository.Get(path));
        }

        public TImage GetCropImage(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new EntityNotFoundException();

            var fileExt = Path.GetExtension(path).ToUpper().Replace(".", string.Empty).Trim();
            if (!PackageConstants.SupportedExtensions.Contains(fileExt))
                throw new NotSupportedExtensionException(fileExt);

            var tImage = new TImage
            {
                Id = path,
                Name = Path.GetFileName(path),
                AbsoluteUrl = path
            };

            return tImage;
        }

        private TImage GetImage(uMedia uMedia)
        {
            _validationService.ValidateExtension(uMedia);
            return Convert(uMedia);
        }

        public void Move(uMedia image, int parentId)
        {
            _imageRepository.Move(image, parentId);
        }

        public async Task OptimizeImageAsync(TImage image)
        {
            _stateService.CreateState(1);
            var tinyResponse = await _tinyPngConnectorService.TinifyAsync(image, base.FileSystem).ConfigureAwait(false);
            if (tinyResponse.Output.Url == null)
            {
                _historyService.CreateResponseHistory(image.Id, tinyResponse);
                return;
            }
            UpdateImageAfterSuccessfullRequest(tinyResponse, image, base.FileSystem);
            SendStatistic();
        }

        /// <summary>
        /// License
        /// Copyright © Backend Devs.
        /// During installation, you agree that we store a total number of images optimized for you.
        /// https://our.umbraco.org/projects/backoffice-extensions/tinifier/
        /// </summary>
        private void SendStatistic()
        {
            try
            {
                var userDomain = HttpContext.Current.Request.Url.Host;
                HostingEnvironment.QueueBackgroundWorkItem(stat => _backendDevsConnectorService.SendStatistic(userDomain));
            }
            catch (Exception)
            {
                return;
            }
        }

        public IEnumerable<TImage> GetOptimizedImages()
        {
            return Convert(_imageRepository.GetOptimizedItems().OrderByDescending(x => x.UpdateDate));
        }

        public IEnumerable<TImage> GetTopOptimizedImages()
        {
            return _imageRepository.GetTopOptimizedImages();
        }

        public IEnumerable<TImage> GetFolderImages(int folderId)
        {
            return base.Convert(_imageRepository.GetItemsFromFolder(folderId));
        }

        public void UpdateImageAfterSuccessfullRequest(TinyResponse tinyResponse, TImage image, IFileSystem fs)
        {
            int.TryParse(image.Id, out var id);

            // download optimized image
            var tImageBytes = TinyImageService.Instance.DownloadImage(tinyResponse.Output.Url);

            // preserve image metadata
            if (_settingsService.GetSettings().PreserveMetadata)
            {
                var originImageBytes = image.ToBytes(fs);
                PreserveImageMetadata(originImageBytes, ref tImageBytes);
            }

            // httpContext is null when optimization on upload
            // https://our.umbraco.org/projects/backoffice-extensions/tinifier/bugs/90472-error-systemargumentnullexception-value-cannot-be-null
            if (HttpContext.Current == null)
                HttpContextHelper.CreateEnsureUmbracoContext();

            // update physical file
            base.UpdateMedia(image, tImageBytes);
            // update history
            _historyService.CreateResponseHistory(image.Id, tinyResponse);
            // update umbraco media attributes
            _imageRepository.Update(id, tinyResponse.Output.Size);
            // update statistic
            _statisticService.UpdateStatistic();
            // update tinifying state
            _stateService.UpdateState();
        }

        public void UndoTinify(int mediaId)
        {
            byte[] imageBytes;
            var originImage = _imageHistoryService.Get(mediaId);

            if (originImage != null)
            {
                var mediaFile = _mediaService.GetById(mediaId) as uMedia;

                if (File.Exists(originImage.OriginFilePath))
                {
                    using (var file = new FileStream(originImage.OriginFilePath, FileMode.Open))
                        imageBytes = SolutionExtensions.ReadFully(file);

                    if (Directory.Exists(Path.GetDirectoryName(originImage.OriginFilePath)))
                        File.Delete(originImage.OriginFilePath);

                    var image = new TImage
                    {
                        Id = mediaId.ToString(),
                        Name = mediaFile.Name,
                        AbsoluteUrl = SolutionExtensions.GetAbsoluteUrl(mediaFile)
                    };

                    // update physical file
                    base.UpdateMedia(image, imageBytes);
                    // update umbraco media attributes
                    _imageRepository.Update(mediaId, imageBytes.Length);
                    _historyService.Delete(mediaId.ToString());
                    // update statistic
                    _statisticService.UpdateStatistic();
                    //delete image history
                    _imageHistoryService.Delete(mediaId);
                }
            }
            else
            {
                throw new UndoTinifierException("Image not optimized or Undo tinify not enabled");
            }
        }

        public void BackupMediaPaths(IEnumerable<uMedia> media)
        {
            foreach (var m in media)
            {
                var mediaHistory = new TinifierMediaHistory
                {
                    MediaId = m.Id,
                    FormerPath = m.Path,
                    OrganizationRootFolderId = m.ParentId
                };
                _mediaHistoryRepository.Create(mediaHistory);
            }
        }

        public void DiscardOrganizing(int folderId)
        {
            if (!IsFolderOrganized(folderId))
                throw new OrganizationConstraintsException("This folder is not an organization root.");

            Discard(folderId);
        }

        protected void PreserveImageMetadata(byte[] originImage, ref byte[] optimizedImage)
        {
            var originImg = (Image)new ImageConverter().ConvertFrom(originImage);
            var optimisedImg = (Image)new ImageConverter().ConvertFrom(optimizedImage);
            var srcPropertyItems = originImg.PropertyItems;
            foreach (var item in srcPropertyItems)
            {
                optimisedImg.SetPropertyItem(item);
            }

            using (var ms = new MemoryStream())
            {
                optimisedImg.Save(ms, optimisedImg.RawFormat);
                optimizedImage = ms.ToArray();
            }
        }

        public bool IsFolderChildOfOrganizedFolder(int sourceFolderId)
        {
            var organizedFoldersList = _mediaHistoryRepository.GetOrganazedFolders();

            while(sourceFolderId != -1 && organizedFoldersList.Any())
            {
                var isOrganized = organizedFoldersList.Contains(sourceFolderId);

                if (isOrganized)
                    return true;

                sourceFolderId = _mediaService.GetById(sourceFolderId).ParentId;
            }

            return organizedFoldersList.Contains(-1);
        }

        private void Discard(int baseFolderId)
        {
            var media = _mediaHistoryRepository.GetAll().Where(m => m.OrganizationRootFolderId == baseFolderId);

            foreach (var m in media)
            {
                var monthFolder = _mediaService.GetParent(m.MediaId);
                if (monthFolder == null)
                    continue;
                var yearFolder = _mediaService.GetParent(monthFolder);
                if (yearFolder == null)
                    continue;

                var folderId = yearFolder.Id;
                // the path is stored as a string with comma separated IDs of media
                // where the last value is ID of current media, penultimate value is ID of its root, etc.
                // the first value is ID of the very root media
                var path = m.FormerPath.Split(',');
                var formerParentId = int.Parse(path[path.Length - 2]);
                var image = _imageRepository.Get(m.MediaId);
                Move(image, formerParentId);

                if (!_mediaService.HasChildren(monthFolder.Id))
                {
                    _mediaService.Delete(_mediaService.GetById(monthFolder.Id));
                }

                if (!_mediaService.HasChildren(yearFolder.Id))
                {
                    _mediaService.Delete(_mediaService.GetById(folderId));
                }
            }

            _mediaHistoryRepository.DeleteAll(baseFolderId);
        }

        private bool IsFolderOrganized(int folderId)
        {
            var organizedFoldersList = _mediaHistoryRepository.GetOrganazedFolders();
            return organizedFoldersList.Contains(folderId);
        }
    }
}