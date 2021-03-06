using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WhatsappMonitor.Shared.Models;
using WhatsappMonitor.API.Context;
using WhatsappMonitor.API.Services;
using System.IO;

namespace WhatsappMonitor.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FoldersController : Controller
    {
        private readonly IFoldersService _repo;
        private readonly IChatsMessageService _chat;
        public FoldersController(MyDbContext iEntitiesService, MyDbContext iChatsService)
        {
            this._repo = new FoldersService(iEntitiesService);
            this._chat = new ChatsMessageService(iChatsService);
        }

        [HttpGet]
        public async Task<ActionResult<List<Folder>>> GetAllFolders()
        {
            return await _repo.GetAllEntitiesAsync();
        }

        [HttpGet("{id}")]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Folder>> GetFolderById(int id)
        {
            return await _repo.GetEntityById(id);
        }

        [HttpPost]
        public async Task<ActionResult<List<Folder>>> AddFolder([FromBody] Folder entity)
        {
            await _repo.AddEntity(entity);
            return await _repo.GetAllEntitiesAsync();
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<List<Folder>>> EditFolder(int id, [FromBody] Folder entity)
        {
            await _repo.UpdateEntity(id, entity);
            return await _repo.GetAllEntitiesAsync();
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult<List<Folder>>> Delete(int id)
        {
            await _repo.DeleteEntity(id);
            return await _repo.GetAllEntitiesAsync();
        }

        [HttpPost("file/{id}")]
        public async Task<int> OnPostUploadInternalAsync(int id)
        {
            var systemTime = DateTime.Now;
            var counter = 0;
            if (HttpContext.Request.Form.Files.Any())
            {
                foreach (var file in HttpContext.Request.Form.Files)
                {
                    counter++;
                    var fileName = file.FileName;

                    using (var memoryStream = new MemoryStream())
                    {
                        await file.CopyToAsync(memoryStream);
                        var fileContent = memoryStream.ToArray();
                        await _repo.UploadFile(id, systemTime, fileName, fileContent);
                    }
                }
            }
            await _chat.ProcessEntityFiles();
            return counter;
        }
    }
}