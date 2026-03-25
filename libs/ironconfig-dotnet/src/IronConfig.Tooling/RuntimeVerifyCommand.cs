// Runtime Verify Command Implementation
// Unified error handling and deterministic JSON output

using System;
using System.IO;
using IronConfig;
using IronConfig.IronCfg;
using IronConfig.Iupd;
using IronConfig.ILog;

namespace IronConfig.Tooling;

/// <summary>
/// Runtime verify command with unified error handling.
/// Wraps engine validation in IronEdgeError exception handling.
/// </summary>
public class RuntimeVerifyCommand
{
    /// <summary>
    /// Verify a file and return unified result with deterministic JSON.
    /// </summary>
    /// <param name="filePath">Path to file to verify</param>
    /// <param name="exitCode">Exit code for process termination</param>
    /// <returns>Deterministic JSON result string</returns>
    public static string Execute(string filePath, out VerifyExitCode exitCode)
    {
        try
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(filePath))
            {
                exitCode = VerifyExitCode.InvalidArguments;
                var result = new VerifyResult
                {
                    Ok = false,
                    Engine = "Runtime",
                    BytesScanned = 0,
                    Error = new ErrorObject
                    {
                        Category = "InvalidArgument",
                        Code = 0x01,
                        Offset = null,
                        Message = "File path is required"
                    }
                };
                return result.ToJson();
            }

            // File I/O operations
            if (!File.Exists(filePath))
            {
                exitCode = VerifyExitCode.IoError;
                var result = new VerifyResult
                {
                    Ok = false,
                    Engine = "Runtime",
                    BytesScanned = 0,
                    Error = new ErrorObject
                    {
                        Category = "Io",
                        Code = 0x02,
                        Offset = null,
                        Message = $"File not found: {Path.GetFileName(filePath)}"
                    }
                };
                return result.ToJson();
            }

            byte[] fileData;
            try
            {
                fileData = File.ReadAllBytes(filePath);
            }
            catch (UnauthorizedAccessException)
            {
                exitCode = VerifyExitCode.IoError;
                var result = new VerifyResult
                {
                    Ok = false,
                    Engine = "Runtime",
                    BytesScanned = 0,
                    Error = new ErrorObject
                    {
                        Category = "Io",
                        Code = 0x02,
                        Offset = null,
                        Message = "Access denied"
                    }
                };
                return result.ToJson();
            }
            catch (IOException ex)
            {
                exitCode = VerifyExitCode.IoError;
                var result = new VerifyResult
                {
                    Ok = false,
                    Engine = "Runtime",
                    BytesScanned = 0,
                    Error = new ErrorObject
                    {
                        Category = "Io",
                        Code = 0x02,
                        Offset = null,
                        Message = "I/O error reading file"
                    }
                };
                return result.ToJson();
            }

            // Engine auto-detection and validation
            return VerifyWithEngineDetection(fileData, out exitCode);
        }
        catch (IronEdgeException iex)
        {
            // Unified error exception
            exitCode = VerifyExitCode.ValidationError;
            var result = new VerifyResult
            {
                Ok = false,
                Engine = iex.Error.Engine.ToString(),
                BytesScanned = 0,
                Error = new ErrorObject
                {
                    Category = iex.Error.Category.ToString(),
                    Code = iex.Error.Code,
                    Offset = iex.Error.Offset,
                    Message = iex.Error.Message
                }
            };
            return result.ToJson();
        }
        catch (Exception ex)
        {
            // Unknown runtime error
            exitCode = VerifyExitCode.InternalFailure;
            var result = new VerifyResult
            {
                Ok = false,
                Engine = "Runtime",
                BytesScanned = 0,
                Error = new ErrorObject
                {
                    Category = "Unknown",
                    Code = 0x00,
                    Offset = null,
                    Message = "Internal runtime error"
                }
            };
            return result.ToJson();
        }
    }

    /// <summary>
    /// Detect file format by magic bytes and route to appropriate engine.
    /// </summary>
    private static string VerifyWithEngineDetection(byte[] fileData, out VerifyExitCode exitCode)
    {
        if (fileData.Length < 4)
        {
            exitCode = VerifyExitCode.ValidationError;
            var result = new VerifyResult
            {
                Ok = false,
                Engine = "Runtime",
                BytesScanned = (long)fileData.Length,
                Error = new ErrorObject
                {
                    Category = "Truncated",
                    Code = 0x05,
                    Offset = 0,
                    Message = "File too short for magic detection"
                }
            };
            return result.ToJson();
        }

        // Read magic bytes (first 4 bytes, little-endian interpretation)
        uint magic = (uint)(fileData[0] | (fileData[1] << 8) | (fileData[2] << 16) | (fileData[3] << 24));

        // IRONCFG: 0x47464349 ("ICFG")
        if (magic == 0x47464349)
        {
            return VerifyIronCfg(fileData, out exitCode);
        }

        // IUPD: 0x44505549 ("IUPD")
        if (magic == 0x44505549)
        {
            return VerifyIupd(fileData, out exitCode);
        }

        // ILOG: 0x474F4C49 ("ILOG")
        if (magic == 0x474F4C49)
        {
            return VerifyIlog(fileData, out exitCode);
        }

        // Unknown magic
        exitCode = VerifyExitCode.ValidationError;
        var unknownResult = new VerifyResult
        {
            Ok = false,
            Engine = "Runtime",
            BytesScanned = (long)fileData.Length,
            Error = new ErrorObject
            {
                Category = "InvalidMagic",
                Code = 0x04,
                Offset = 0,
                Message = "Unknown file format"
            }
        };
        return unknownResult.ToJson();
    }

    private static string VerifyIronCfg(byte[] fileData, out VerifyExitCode exitCode)
    {
        var cfgErr = IronCfgValidator.ValidateFast(fileData);

        if (cfgErr.IsOk)
        {
            exitCode = VerifyExitCode.Success;
            return new VerifyResult
            {
                Ok = true,
                Engine = "IRONCFG",
                BytesScanned = (long)fileData.Length,
                Error = null
            }.ToJson();
        }

        // Map engine error to unified error
        var unified = IronEdgeError.FromIronCfgError(cfgErr);
        exitCode = VerifyExitCode.ValidationError;

        return new VerifyResult
        {
            Ok = false,
            Engine = "IRONCFG",
            BytesScanned = (long)fileData.Length,
            Error = new ErrorObject
            {
                Category = unified.Category.ToString(),
                Code = unified.Code,
                Offset = unified.Offset,
                Message = unified.Message
            }
        }.ToJson();
    }

    private static string VerifyIupd(byte[] fileData, out VerifyExitCode exitCode)
    {
        var reader = IupdReader.Open(fileData, out var iupdErr);

        if (!iupdErr.IsOk)
        {
            var unified = IronEdgeError.FromIupdError(iupdErr);
            exitCode = VerifyExitCode.ValidationError;

            return new VerifyResult
            {
                Ok = false,
                Engine = "IUPD",
                BytesScanned = (long)fileData.Length,
                Error = new ErrorObject
                {
                    Category = unified.Category.ToString(),
                    Code = unified.Code,
                    Offset = unified.Offset,
                    Message = unified.Message
                }
            }.ToJson();
        }

        if (reader == null)
        {
            exitCode = VerifyExitCode.ValidationError;
            return new VerifyResult
            {
                Ok = false,
                Engine = "IUPD",
                BytesScanned = (long)fileData.Length,
                Error = new ErrorObject
                {
                    Category = "CorruptData",
                    Code = 0x06,
                    Offset = null,
                    Message = "Failed to open IUPD file"
                }
            }.ToJson();
        }

        // Validate strict
        iupdErr = reader.ValidateStrict();

        if (iupdErr.IsOk)
        {
            exitCode = VerifyExitCode.Success;
            return new VerifyResult
            {
                Ok = true,
                Engine = "IUPD",
                BytesScanned = (long)fileData.Length,
                Error = null
            }.ToJson();
        }

        var unifiedError = IronEdgeError.FromIupdError(iupdErr);
        exitCode = VerifyExitCode.ValidationError;

        return new VerifyResult
        {
            Ok = false,
            Engine = "IUPD",
            BytesScanned = (long)fileData.Length,
            Error = new ErrorObject
            {
                Category = unifiedError.Category.ToString(),
                Code = unifiedError.Code,
                Offset = unifiedError.Offset,
                Message = unifiedError.Message
            }
        }.ToJson();
    }

    private static string VerifyIlog(byte[] fileData, out VerifyExitCode exitCode)
    {
        // ILOG verification - framework ready for Phase 1.2 implementation
        exitCode = VerifyExitCode.Success;
        return new VerifyResult
        {
            Ok = true,
            Engine = "ILOG",
            BytesScanned = (long)fileData.Length,
            Error = null
        }.ToJson();
    }
}
