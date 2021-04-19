using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Threading.Tasks;
using BililiveRecorder.Flv;
using BililiveRecorder.Flv.Parser;
using BililiveRecorder.Flv.Xml;
using Serilog;

namespace BililiveRecorder.ToolBox.Commands
{
    public class ExportRequest : ICommandRequest<ExportResponse>
    {
        public string Input { get; set; } = string.Empty;

        public string Output { get; set; } = string.Empty;
    }

    public class ExportResponse
    {
    }

    public class ExportHandler : ICommandHandler<ExportRequest, ExportResponse>
    {
        private static readonly ILogger logger = Log.ForContext<ExportHandler>();

        public Task<CommandResponse<ExportResponse>> Handle(ExportRequest request) => this.Handle(request, null);

        public async Task<CommandResponse<ExportResponse>> Handle(ExportRequest request, Func<double, Task>? progress)
        {
            FileStream? inputStream = null, outputStream = null;
            try
            {
                try
                {
                    inputStream = File.Open(request.Input, FileMode.Open, FileAccess.Read, FileShare.Read);
                }
                catch (Exception ex)
                {
                    return new CommandResponse<ExportResponse>
                    {
                        Status = ResponseStatus.InputIOError,
                        Exception = ex,
                        ErrorMessage = ex.Message
                    };
                }

                try
                {
                    outputStream = File.OpenWrite(request.Output);
                }
                catch (Exception ex)
                {
                    return new CommandResponse<ExportResponse>
                    {
                        Status = ResponseStatus.OutputIOError,
                        Exception = ex,
                        ErrorMessage = ex.Message
                    };
                }

                var tags = await Task.Run(async () =>
                {
                    var count = 0;
                    var tags = new List<Tag>();
                    using var reader = new FlvTagPipeReader(PipeReader.Create(inputStream), new DefaultMemoryStreamProvider(), skipData: true, logger: logger);
                    while (true)
                    {
                        var tag = await reader.ReadTagAsync(default).ConfigureAwait(false);
                        if (tag is null) break;
                        tags.Add(tag);

                        if (count++ % 300 == 0 && progress is not null)
                            await progress((double)inputStream.Position / inputStream.Length);
                    }
                    return tags;
                });

                await Task.Run(() =>
                {
                    using var writer = new StreamWriter(new GZipStream(outputStream, CompressionLevel.Optimal));
                    XmlFlvFile.Serializer.Serialize(writer, new XmlFlvFile
                    {
                        Tags = tags
                    });
                });

                return new CommandResponse<ExportResponse> { Status = ResponseStatus.OK, Result = new ExportResponse() };
            }
            catch (NotFlvFileException ex)
            {
                return new CommandResponse<ExportResponse>
                {
                    Status = ResponseStatus.NotFlvFile,
                    Exception = ex,
                    ErrorMessage = ex.Message
                };
            }
            catch (UnknownFlvTagTypeException ex)
            {
                return new CommandResponse<ExportResponse>
                {
                    Status = ResponseStatus.UnknownFlvTagType,
                    Exception = ex,
                    ErrorMessage = ex.Message
                };
            }
            catch (Exception ex)
            {
                return new CommandResponse<ExportResponse>
                {
                    Status = ResponseStatus.Error,
                    Exception = ex,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                inputStream?.Dispose();
                outputStream?.Dispose();
            }
        }

        public void PrintResponse(ExportResponse response) => Console.WriteLine("OK");
    }
}
