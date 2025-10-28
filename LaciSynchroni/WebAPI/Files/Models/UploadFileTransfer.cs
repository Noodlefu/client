using System;
using LaciSynchroni.Common.Dto.Files;

namespace LaciSynchroni.WebAPI.Files.Models;

public class UploadFileTransfer : FileTransfer
{
    public UploadFileTransfer(UploadFileDto dto, Guid serverUuid) : base(dto, serverUuid)
    {
    }

    public string LocalFile { get; set; } = string.Empty;
    public override long Total { get; set; }
}