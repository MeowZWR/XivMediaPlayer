using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.D3DCompiler;

namespace XivMediaPlayer.Compositing {
  /// <summary>
  /// Renders a textured quad with shader-based depth occlusion to an offscreen
  /// render target. The result is an RGBA texture with transparent pixels where
  /// game geometry occludes the quad. This texture is then displayed via ImGui.
  /// </summary>
  internal unsafe class DepthTestedRenderer : IDisposable {
    private ID3D11Device _device;
    private ID3D11DeviceContext _context;

    // Pipeline state
    private ID3D11VertexShader _vertexShader;
    private ID3D11PixelShader _pixelShader;
    private ID3D11InputLayout _inputLayout;
    private ID3D11RasterizerState _rasterizerState;
    private ID3D11BlendState _blendState;
    private ID3D11SamplerState _videoSampler;
    private ID3D11SamplerState _depthSampler;

    // Buffers
    private ID3D11Buffer _vertexBuffer;
    private ID3D11Buffer _indexBuffer;
    private ID3D11Buffer _constantBuffer;

    // Offscreen render target
    private ID3D11Texture2D _renderTarget;
    private ID3D11RenderTargetView _renderTargetView;
    private ID3D11ShaderResourceView _renderTargetSRV;
    private int _rtWidth, _rtHeight;

    private bool _initialized;
    private bool _disposed;
    private string _initError;

    [StructLayout(LayoutKind.Sequential)]
    private struct QuadVertex {
      public Vector3 Position;
      public Vector2 TexCoord;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VSConstants {
      public Matrix4x4 ViewProjection;
      public Vector2 ScreenSize;
      public Vector2 _pad;
    }

    private static readonly ushort[] QuadIndices = { 0, 1, 2, 2, 3, 0 };

    private const string ShaderCode = @"
cbuffer Constants : register(b0) {
  row_major matrix ViewProjection;
  float2 ScreenSize;
  float2 _pad;
};

Texture2D VideoTexture : register(t0);
Texture2D DepthTexture : register(t1);
SamplerState VideoSampler : register(s0);
SamplerState DepthSampler : register(s1);

struct VS_IN {
  float3 pos : POSITION;
  float2 uv : TEXCOORD;
};

struct PS_IN {
  float4 pos : SV_POSITION;
  float2 uv : TEXCOORD;
};

PS_IN VS(VS_IN input) {
  PS_IN output = (PS_IN)0;
  output.pos = mul(float4(input.pos, 1.0f), ViewProjection);
  output.uv = input.uv;
  return output;
}

float4 PS(PS_IN input) : SV_TARGET {
  float4 color = VideoTexture.Sample(VideoSampler, input.uv);

  // Convert SV_POSITION (pixel coords) to depth buffer UV
  float2 screenUV = input.pos.xy / ScreenSize;

  // Sample the game's depth buffer
  float gameDepth = DepthTexture.Sample(DepthSampler, screenUV).r;

  // input.pos.z is the quad's depth after VP transform
  float quadDepth = input.pos.z;

  // The game uses reverse-Z: closer objects have HIGHER depth values.
  // Occlude when game geometry is closer (gameDepth > quadDepth).
  if (gameDepth > quadDepth + 0.0001) {
    color.a = 0;
  }

  return color;
}
";

    public bool IsInitialized => _initialized;
    public string InitError => _initError;

    /// <summary>
    /// Returns the SRV of the offscreen render target, for use as an ImGui texture.
    /// </summary>
    public ID3D11ShaderResourceView OutputSRV => _renderTargetSRV;
    public int OutputWidth => _rtWidth;
    public int OutputHeight => _rtHeight;

    public bool Initialize() {
      if (_initialized || _disposed) return _initialized;

      try {
        var ffxivDevice = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        if (ffxivDevice == null || ffxivDevice->D3D11DeviceContext == null) {
          _initError = "FFXIV D3D11 device context not available.";
          return false;
        }

        _context = new ID3D11DeviceContext((IntPtr)ffxivDevice->D3D11DeviceContext);
        _device = _context.Device;

        // Compile shaders
        var vsBytecode = Compiler.Compile(ShaderCode, "VS", "", "vs_5_0");
        _vertexShader = _device.CreateVertexShader(vsBytecode.Span);

        var inputElements = new[] {
          new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
          new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12, 0),
        };
        _inputLayout = _device.CreateInputLayout(inputElements, vsBytecode.Span);

        var psBytecode = Compiler.Compile(ShaderCode, "PS", "", "ps_5_0");
        _pixelShader = _device.CreatePixelShader(psBytecode.Span);

        // Constant buffer (VP matrix + screen size = 64 + 16 = 80 bytes)
        _constantBuffer = _device.CreateBuffer(new BufferDescription {
          ByteWidth = 80,
          Usage = ResourceUsage.Default,
          BindFlags = BindFlags.ConstantBuffer,
          CPUAccessFlags = CpuAccessFlags.None,
        });

        // Vertex buffer (4 vertices, updated each frame)
        _vertexBuffer = _device.CreateBuffer(new BufferDescription {
          ByteWidth = Marshal.SizeOf<QuadVertex>() * 4,
          Usage = ResourceUsage.Default,
          BindFlags = BindFlags.VertexBuffer,
          CPUAccessFlags = CpuAccessFlags.None,
        });

        // Index buffer (6 indices, static)
        _indexBuffer = _device.CreateBuffer(QuadIndices, BindFlags.IndexBuffer);

        // Blend state: premultiplied alpha output
        var blendDesc = new BlendDescription();
        blendDesc.RenderTarget[0] = new RenderTargetBlendDescription {
          BlendEnable = false,
          RenderTargetWriteMask = ColorWriteEnable.All,
        };
        _blendState = _device.CreateBlendState(blendDesc);

        // Rasterizer: no culling
        _rasterizerState = _device.CreateRasterizerState(new RasterizerDescription {
          FillMode = FillMode.Solid,
          CullMode = CullMode.None,
          FrontCounterClockwise = false,
          DepthClipEnable = false, // Don't clip — we handle depth in shader
          ScissorEnable = false,
        });

        // Samplers
        _videoSampler = _device.CreateSamplerState(new SamplerDescription {
          Filter = Filter.MinMagMipLinear,
          AddressU = TextureAddressMode.Clamp,
          AddressV = TextureAddressMode.Clamp,
          AddressW = TextureAddressMode.Clamp,
        });
        _depthSampler = _device.CreateSamplerState(new SamplerDescription {
          Filter = Filter.MinMagMipPoint,
          AddressU = TextureAddressMode.Clamp,
          AddressV = TextureAddressMode.Clamp,
          AddressW = TextureAddressMode.Clamp,
        });

        _initialized = true;
        return true;
      } catch (Exception ex) {
        _initError = $"DepthTestedRenderer init failed: {ex.Message}";
        return false;
      }
    }

    /// <summary>
    /// Ensures the offscreen render target is the correct size.
    /// </summary>
    private void EnsureRenderTarget(int width, int height) {
      if (_renderTarget != null && _rtWidth == width && _rtHeight == height) return;

      _renderTargetView?.Dispose();
      _renderTargetSRV?.Dispose();
      _renderTarget?.Dispose();

      _rtWidth = width;
      _rtHeight = height;

      _renderTarget = _device.CreateTexture2D(new Texture2DDescription {
        Width = width,
        Height = height,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.R8G8B8A8_UNorm,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        CPUAccessFlags = CpuAccessFlags.None,
      });

      _renderTargetView = _device.CreateRenderTargetView(_renderTarget);
      _renderTargetSRV = _device.CreateShaderResourceView(_renderTarget);
    }

    /// <summary>
    /// Render the video quad with depth occlusion to the offscreen render target.
    /// After calling this, use OutputSRV to display the result in ImGui.
    /// Returns true if rendering succeeded.
    /// </summary>
    public bool Render(
      (Vector3 tl, Vector3 tr, Vector3 br, Vector3 bl) corners,
      IntPtr videoTextureSRV,
      Matrix4x4 viewProjection,
      ID3D11ShaderResourceView depthSRV,
      int screenWidth, int screenHeight) {

      if (!_initialized || _disposed || videoTextureSRV == IntPtr.Zero || depthSRV == null) return false;

      EnsureRenderTarget(screenWidth, screenHeight);

      // Save current pipeline state
      var savedRTVs = new ID3D11RenderTargetView[1];
      ID3D11DepthStencilView savedDSV;
      _context.OMGetRenderTargets(1, savedRTVs, out savedDSV);

      try {
        // Clear our render target to fully transparent
        _context.ClearRenderTargetView(_renderTargetView, new Vortice.Mathematics.Color4(0, 0, 0, 0));

        // Update vertex buffer
        var vertices = new QuadVertex[] {
          new() { Position = corners.tl, TexCoord = new Vector2(0, 0) },
          new() { Position = corners.tr, TexCoord = new Vector2(1, 0) },
          new() { Position = corners.br, TexCoord = new Vector2(1, 1) },
          new() { Position = corners.bl, TexCoord = new Vector2(0, 1) },
        };
        _context.UpdateSubresource(vertices, _vertexBuffer);

        // Update constant buffer
        var constants = new VSConstants {
          ViewProjection = viewProjection,
          ScreenSize = new Vector2(screenWidth, screenHeight),
        };
        _context.UpdateSubresource(constants, _constantBuffer);

        // Set pipeline
        _context.IASetInputLayout(_inputLayout);
        _context.IASetVertexBuffer(0, _vertexBuffer, Marshal.SizeOf<QuadVertex>());
        _context.IASetIndexBuffer(_indexBuffer, Format.R16_UInt, 0);
        _context.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);

        _context.VSSetShader(_vertexShader);
        _context.VSSetConstantBuffer(0, _constantBuffer);

        _context.PSSetShader(_pixelShader);
        _context.PSSetConstantBuffer(0, _constantBuffer);
        var videoSRV = new ID3D11ShaderResourceView(videoTextureSRV);
        _context.PSSetShaderResource(0, videoSRV);
        _context.PSSetShaderResource(1, depthSRV);
        _context.PSSetSampler(0, _videoSampler);
        _context.PSSetSampler(1, _depthSampler);

        _context.RSSetState(_rasterizerState);
        _context.OMSetBlendState(_blendState);

        // Set viewport and render target to our offscreen texture
        _context.RSSetViewport(0, 0, screenWidth, screenHeight);
        _context.OMSetRenderTargets(_renderTargetView);

        // Draw
        _context.DrawIndexed(6, 0, 0);

        return true;
      } finally {
        // Restore
        _context.OMSetRenderTargets(savedRTVs, savedDSV);
        _context.PSSetShaderResource(1, (ID3D11ShaderResourceView)null);
      }
    }

    public void Dispose() {
      if (_disposed) return;
      _disposed = true;

      _renderTargetView?.Dispose();
      _renderTargetSRV?.Dispose();
      _renderTarget?.Dispose();
      _depthSampler?.Dispose();
      _videoSampler?.Dispose();
      _blendState?.Dispose();
      _rasterizerState?.Dispose();
      _indexBuffer?.Dispose();
      _vertexBuffer?.Dispose();
      _constantBuffer?.Dispose();
      _inputLayout?.Dispose();
      _pixelShader?.Dispose();
      _vertexShader?.Dispose();
    }
  }
}
