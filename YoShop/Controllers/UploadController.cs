﻿using Masuit.Tools;
using Masuit.Tools.AspNetCore.Mime;
using Masuit.Tools.Logging;
using Masuit.Tools.Systems;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using YoShop.Extensions;
using YoShop.Models;
using YoShop.Models.Requests;

namespace YoShop.Controllers
{
    /// <summary>
    /// 文件上传
    /// </summary>
    [ApiExplorerSettings(IgnoreApi = true)]
    public class UploadController : SellerBaseController
    {
        private readonly IWebHostEnvironment _hostingEnvironment;

        private readonly IFreeSql _fsql;

        /// <summary>
        /// 文件上传
        /// </summary>
        /// <param name="hostingEnvironment"></param>
        /// <param name="fsql"></param>
        public UploadController(IWebHostEnvironment hostingEnvironment, IFreeSql fsql)
        {
            _hostingEnvironment = hostingEnvironment;
            _fsql = fsql;
        }

        #region 文件库文件管理

        /// <summary>
        /// 文件库列表
        /// </summary>
        /// <param name="request"></param>
        /// <param name="type"></param>
        /// <param name="groupId"></param>
        /// <returns></returns>
        [HttpGet, Route("/upload.library/fileList")]
        public async Task<IActionResult> LibraryFileList(FilePageRequest request, string type = "image", int groupId = -1)
        {
            var groupList = await GetUploadGroupList(type);
            var data = await GetUploadFileList(request, groupId, type);
            request.Data = data;
            request.LastPage = (int)Math.Ceiling(request.Total * 1.0f / request.PerPage * 1.0f);
            return YesResult(new { groupList = groupList, fileList = request });
        }

        /// <summary>
        /// 通用图片文件上传 (多文件)
        /// </summary>
        /// <returns></returns>
        [HttpPost, Route("/upload/image")]
        public async Task<IActionResult> UploadImage(IFormFile file, FileUploadRequest request)
        {
            //var file = Request.Form.Files[0];
            UploadFile upload;
            try
            {
                var result = await FileUpload(file);
                if (result.Code == 0) return No(result.Msg);
                upload = new UploadFile
                {
                    CreateTime = DateTimeExtensions.GetCurrentTimeStamp(),
                    UpdateTime = DateTimeExtensions.GetCurrentTimeStamp(),
                    FileType = "image",
                    Storage = "local",
                    IsDelete = 0,
                    GroupId = request.GroupId,
                    FileSize = request.Size,
                    FileUrl = result.Msg,
                    FileName = request.Name,
                    Extension = Path.GetExtension(request.Name).TrimStart('.'),
                    WxappId = GetSellerSession().WxappId
                };

                await _fsql.Insert<UploadFile>().AppendData(upload).ExecuteAffrowsAsync();
            }
            catch (Exception e)
            {
                LogManager.Error(GetType(), e);
                return No(e.Message);
            }
            return YesResult("图片上传成功！", upload);
        }

        /// <summary>
        /// 文件库移动文件
        /// </summary>
        /// <param name="groupId"></param>
        /// <param name="fileIds"></param>
        /// <returns></returns>
        [HttpPost, Route("/upload.library/moveFiles")]
        public async Task<IActionResult> LibraryMoveFiles(uint groupId, uint[] fileIds)
        {
            try
            {
                if (fileIds.Length > 0)
                {
                    await _fsql.Update<UploadFile>().Set(l => l.GroupId == groupId).Where(l => fileIds.Contains(l.FileId))
                         .ExecuteAffrowsAsync();
                }
            }
            catch (Exception e)
            {
                LogManager.Error(GetType(), e);
                return No(e.Message);
            }
            return Yes("移动成功！");
        }

        /// <summary>
        /// 文件库删除文件
        /// </summary>
        /// <returns></returns>
        [HttpPost, Route("/upload.library/deleteFiles")]
        public async Task<IActionResult> LibraryDeleteFiles(uint[] fileIds)
        {
            try
            {
                if (fileIds.Length > 0)
                {
                    await _fsql.Update<UploadFile>().Set(l => l.IsDelete == 0).Where(l => fileIds.Contains(l.FileId))
                         .ExecuteAffrowsAsync();
                }
            }
            catch (Exception e)
            {
                LogManager.Error(GetType(), e);
                return No(e.Message);
            }
            return Yes("删除成功！");
        }

        private async Task<List<UploadFile>> GetUploadFileList(FilePageRequest request, int groupId, string type = "image")
        {
            var select = _fsql.Select<UploadFile>();
            return groupId == -1
                ? await @select.Where(l => l.FileType == type && l.IsDelete == 0).OrderBy(l => l.FileId)
                    .Page(request.CurPage, request.PerPage).ToListAsync()
                : await @select.Where(l => l.FileType == type && l.IsDelete == 0 && l.GroupId == groupId)
                    .OrderBy(l => l.FileId).Page(request.CurPage, request.PerPage).ToListAsync();
        }

        /// <summary>
        /// 通用文件上传
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        private async Task<ResultInfo> FileUpload(IFormFile file)
        {
            if (file == null)
                return FailResp("上传文件为空！");
            string filename = $"{DateTime.Now:yyyyMMddHHmmss}" + SnowFlake.GetInstance().GetUniqueShortId(9) + Path.GetExtension(file.FileName);
            string path;
            switch (file.ContentType)
            {
                case var _ when file.ContentType.StartsWith("image"):
                    path = Path.Combine(_hostingEnvironment.WebRootPath, "upload", "images", $"{DateTime.Now:yyyy}/{DateTime.Now:MM}/{DateTime.Now:dd}", filename);
                    break;
                case var _ when file.ContentType.StartsWith("audio") || file.ContentType.StartsWith("video"):
                    path = Path.Combine(_hostingEnvironment.WebRootPath, "upload", "media", $"{DateTime.Now:yyyy}/{DateTime.Now:MM}/{DateTime.Now:dd}", filename);
                    break;
                case var _ when file.ContentType.StartsWith("text") || (ContentType.Doc + "," + ContentType.Xls + "," + ContentType.Ppt + "," + ContentType.Pdf).Contains(file.ContentType):
                    path = Path.Combine(_hostingEnvironment.WebRootPath, "upload", "docs", $"{DateTime.Now:yyyy}/{DateTime.Now:MM}/{DateTime.Now:dd}", filename);
                    break;
                default:
                    path = Path.Combine(_hostingEnvironment.WebRootPath, "upload", "files", $"{DateTime.Now:yyyy}/{DateTime.Now:MM}/{DateTime.Now:dd}", filename);
                    break;
            }
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                using (FileStream fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                {
                    await file.CopyToAsync(fs);
                }
                return SuccResp(path.Substring(_hostingEnvironment.WebRootPath.Length).Replace("\\", "/"));
            }
            catch (Exception e)
            {
                LogManager.Error(GetType(), e);
                return FailResp("文件上传失败！");
            }
        }

        #endregion

        #region 文件库文件分组管理

        /// <summary>
        /// 添加文件组
        /// </summary>
        /// <returns></returns>
        [HttpPost, Route("/upload.library/addGroup")]
        public async Task<IActionResult> LibraryAddGroup(UploadGroupRequest request)
        {
            var timestamp = DateTime.Now.ConvertToTimeStamp();

            var model = new UploadGroup
            {
                GroupName = request.GroupName,
                GroupType = request.GroupType,
                Sort = 100,
                WxappId = GetSellerSession().WxappId,
                CreateTime = timestamp,
                UpdateTime = timestamp
            };
            long groupId;
            try
            {
                groupId = await _fsql.Insert<UploadGroup>().AppendData(model).ExecuteIdentityAsync();
            }
            catch (Exception e)
            {
                LogManager.Error(GetType(), e);
                return No(e.Message);
            }

            return YesResult("添加成功！", new { groupId, request.GroupName });
        }

        /// <summary>
        /// 编辑文件组
        /// </summary>
        /// <returns></returns>
        [HttpPost, Route("/upload.library/editGroup")]
        public async Task<IActionResult> LibraryEditGroup(UploadGroupRequest request)
        {
            try
            {
                var model = await _fsql.Select<UploadGroup>().Where(l => l.GroupId == request.GroupId).ToOneAsync();
                if (model == null) return No("文件分组不存在或已被删除");
                model.GroupName = request.GroupName;
                model.UpdateTime = DateTime.Now.ConvertToTimeStamp();
                await _fsql.Update<UploadGroup>().SetSource(model).ExecuteAffrowsAsync();
            }
            catch (Exception e)
            {
                LogManager.Error(GetType(), e);
                return No(e.Message);
            }
            return Yes("编辑成功！");
        }

        /// <summary>
        /// 删除文件组
        /// </summary>
        /// <returns></returns>
        [HttpPost, Route("/upload.library/deleteGroup")]
        public async Task<IActionResult> LibraryDeleteGroup(int groupId)
        {
            try
            {
                var _ = await _fsql.Delete<UploadGroup>().Where(l => l.GroupId == groupId).ExecuteAffrowsAsync();
                if (_ > 0)
                    await _fsql.Update<UploadFile>().Set(l => l.GroupId == 0).Where(l => l.GroupId == groupId).ExecuteAffrowsAsync();
            }
            catch (Exception e)
            {
                LogManager.Error(GetType(), e);
                return No(e.Message);
            }

            return Yes("删除成功！");
        }

        private async Task<List<UploadGroup>> GetUploadGroupList(string groupType = "image")
        {
            return await _fsql.Select<UploadGroup>().Where(l => l.GroupType == groupType).OrderBy(l => l.Sort).ToListAsync();
        }

        #endregion
    }
}