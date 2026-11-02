using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ollama
{
    /// <summary> https://github.com/ollama/ollama/blob/main/docs/api.md </summary>
    public static partial class Ollama
    {
        private const string SERVER = "http://localhost:11434/";
        private const int RequestTimeoutMilliseconds = 30000;
        private const int ReadWriteTimeoutMilliseconds = 60000;

        private static class Endpoints
        {
            public const string GENERATE = "api/generate";
            public const string CHAT = "api/chat";
            public const string LIST = "api/tags";
            public const string EMBEDDINGS = "api/embed";
        }

        private static HttpWebRequest CreateRequest(string url, string method)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.ContentType = "application/json";
            request.Method = method;
            request.Timeout = RequestTimeoutMilliseconds;
            request.ReadWriteTimeout = ReadWriteTimeoutMilliseconds;
            return request;
        }

        private static async Task<T> WaitForRequestAsync<T>(
            Task<T> requestTask, HttpWebRequest request, int timeoutMilliseconds, string operation)
        {
            Task completed = await Task.WhenAny(requestTask, Task.Delay(timeoutMilliseconds))
                .ConfigureAwait(false);
            if (completed != requestTask)
            {
                request.Abort();
                throw new TimeoutException(
                    $"Ollama {operation} did not respond within {timeoutMilliseconds / 1000} seconds.");
            }

            return await requestTask.ConfigureAwait(false);
        }

        private static async Task WaitForRequestAsync(
            Task requestTask, HttpWebRequest request, int timeoutMilliseconds, string operation)
        {
            Task completed = await Task.WhenAny(requestTask, Task.Delay(timeoutMilliseconds))
                .ConfigureAwait(false);
            if (completed != requestTask)
            {
                request.Abort();
                throw new TimeoutException(
                    $"Ollama {operation} did not respond within {timeoutMilliseconds / 1000} seconds.");
            }

            await requestTask.ConfigureAwait(false);
        }

        private static async Task<T> PostRequest<T>(string payload, string endpoint) where T : Response.Base
        {
            HttpWebRequest httpWebRequest;

            try
            {
                httpWebRequest = CreateRequest($"{SERVER}{endpoint}", "POST");

                using (var streamWriter = new StreamWriter(await WaitForRequestAsync(
                           httpWebRequest.GetRequestStreamAsync(), httpWebRequest,
                           RequestTimeoutMilliseconds, "request stream").ConfigureAwait(false)))
                    await WaitForRequestAsync(streamWriter.WriteAsync(payload), httpWebRequest,
                        RequestTimeoutMilliseconds, "request write").ConfigureAwait(false);

                string result;

                using (var httpResponse = await WaitForRequestAsync(
                           httpWebRequest.GetResponseAsync(), httpWebRequest,
                           RequestTimeoutMilliseconds, "response").ConfigureAwait(false))
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                    result = await WaitForRequestAsync(streamReader.ReadToEndAsync(), httpWebRequest,
                        ReadWriteTimeoutMilliseconds, "response body").ConfigureAwait(false);

                return JsonConvert.DeserializeObject<T>(result);
            }
            catch (WebException webEx)
            {
                string errorResponse = "";

                if (webEx.Response != null)
                {
                    using (var errorStream = webEx.Response.GetResponseStream())
                    using (var reader = new StreamReader(errorStream))
                        errorResponse = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                Debug.LogError($"HTTP Error during \"{endpoint}\" PostRequest:\n{webEx.Message}\n{errorResponse}\n{webEx.StackTrace}");
                return default;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during \"{endpoint}\" PostRequest:\n{e.Message}\n{e.StackTrace}");
                return default;
            }
        }

        private static async Task PostRequestStream<T>(string payload, string endpoint, Action<T> onChunkReceived) where T : Response.BaseResponse
        {
            HttpWebRequest httpWebRequest;

            try
            {
                httpWebRequest = CreateRequest($"{SERVER}{endpoint}", "POST");

                using (var streamWriter = new StreamWriter(await WaitForRequestAsync(
                           httpWebRequest.GetRequestStreamAsync(), httpWebRequest,
                           RequestTimeoutMilliseconds, "request stream").ConfigureAwait(false)))
                    await WaitForRequestAsync(streamWriter.WriteAsync(payload), httpWebRequest,
                        RequestTimeoutMilliseconds, "request write").ConfigureAwait(false);

                bool isEnd = false;
                int it = 0;

                using (var httpResponse = await WaitForRequestAsync(
                           httpWebRequest.GetResponseAsync(), httpWebRequest,
                           RequestTimeoutMilliseconds, "response").ConfigureAwait(false))
                using (var responseStream = httpResponse.GetResponseStream())
                using (var reader = new StreamReader(responseStream, Encoding.UTF8))
                    while (!isEnd)
                    {
                        it++;
                        string result = await WaitForRequestAsync(reader.ReadLineAsync(),
                            httpWebRequest, ReadWriteTimeoutMilliseconds, "stream data")
                            .ConfigureAwait(false);
                        if (result == null)
                            throw new EndOfStreamException("Ollama closed the response before the stream was complete.");
                        var response = JsonConvert.DeserializeObject<T>(result);
                        if (response == null)
                            throw new InvalidDataException("Ollama returned an empty chat response.");
                        if (it > MaxIterations)
                        {
                            Debug.LogError($"Stream has reached {MaxIterations} iterations... Probably server error?");
                            response.done = true;
                        }

                        onChunkReceived?.Invoke(response);
                        isEnd = response.done;
                    }
            }
            catch (WebException webEx)
            {
                string errorResponse = "";

                if (webEx.Response != null)
                {
                    using (var errorStream = webEx.Response.GetResponseStream())
                    using (var reader = new StreamReader(errorStream))
                        errorResponse = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                Debug.LogError($"HTTP Error during \"{endpoint}\" PostRequest:\n{webEx.Message}\n{errorResponse}\n{webEx.StackTrace}");
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during \"{endpoint}\" PostRequestStream:\n{e.Message}\n{e.StackTrace}");
                throw;
            }
        }

        private static async Task<T> GetRequest<T>(string endpoint) where T : Response.Base
        {
            HttpWebRequest httpWebRequest;

            try
            {
                httpWebRequest = CreateRequest($"{SERVER}{endpoint}", "GET");

                string result;

                using (var httpResponse = await WaitForRequestAsync(
                           httpWebRequest.GetResponseAsync(), httpWebRequest,
                           RequestTimeoutMilliseconds, "response").ConfigureAwait(false))
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                    result = await WaitForRequestAsync(streamReader.ReadToEndAsync(), httpWebRequest,
                        ReadWriteTimeoutMilliseconds, "response body").ConfigureAwait(false);

                return JsonConvert.DeserializeObject<T>(result);
            }
            catch (WebException webEx)
            {
                string errorResponse = "";

                if (webEx.Response != null)
                {
                    using (var errorStream = webEx.Response.GetResponseStream())
                    using (var reader = new StreamReader(errorStream))
                        errorResponse = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                Debug.LogError($"HTTP Error during \"{endpoint}\" PostRequest:\n{webEx.Message}\n{errorResponse}\n{webEx.StackTrace}");
                return default;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during \"{endpoint}\" GetRequest:\n{e.Message}\n{e.StackTrace}");
                return default;
            }
        }
    }
}
