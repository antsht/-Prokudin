#include <cuda_runtime.h>
#include <stdint.h>

#if defined(_WIN32)
#define PROKUDIN_CUDA_API extern "C" __declspec(dllexport)
#else
#define PROKUDIN_CUDA_API extern "C" __attribute__((visibility("default")))
#endif

namespace
{
    constexpr int ThreadsPerBlock = 256;

    __device__ float clamp01(float value)
    {
        return fminf(fmaxf(value, 0.0f), 1.0f);
    }

    __global__ void detectDefectMaskKernel(
        const float* target,
        const float* other1,
        const float* other2,
        const float* targetHighPass,
        const float* other1HighPass,
        const float* other2HighPass,
        int length,
        double coefficientA,
        double coefficientB,
        double coefficientC,
        float residualThreshold,
        float highPassThreshold,
        float supportMultiplier,
        float supportOffset,
        uint8_t* outputMask)
    {
        const int index = (blockIdx.x * blockDim.x) + threadIdx.x;
        if (index >= length)
        {
            return;
        }

        const float predicted = clamp01(static_cast<float>(
            (coefficientA * static_cast<double>(other1[index])) +
            (coefficientB * static_cast<double>(other2[index])) +
            coefficientC));
        const float residual = fabsf(target[index] - predicted);
        const float otherSupport = fmaxf(other1HighPass[index], other2HighPass[index]);
        outputMask[index] =
            residual > residualThreshold &&
            targetHighPass[index] > highPassThreshold &&
            targetHighPass[index] > (otherSupport * supportMultiplier) + supportOffset
                ? 1
                : 0;
    }

    __global__ void predictMaskedKernel(
        const float* target,
        const float* guide1,
        const float* guide2,
        const uint8_t* defectMask,
        int length,
        double coefficientA,
        double coefficientB,
        double coefficientC,
        float* output)
    {
        const int index = (blockIdx.x * blockDim.x) + threadIdx.x;
        if (index >= length)
        {
            return;
        }

        output[index] = defectMask[index] == 0
            ? target[index]
            : clamp01(static_cast<float>(
                (coefficientA * static_cast<double>(guide1[index])) +
                (coefficientB * static_cast<double>(guide2[index])) +
                coefficientC));
    }

    __device__ int clampIndex(int value, int limit)
    {
        return value < 0 ? 0 : value >= limit ? limit - 1 : value;
    }

    __global__ void blurHorizontalKernel(
        const float* source,
        float* destination,
        int width,
        int height,
        int radius,
        const float* weights)
    {
        const int x = (blockIdx.x * blockDim.x) + threadIdx.x;
        const int y = (blockIdx.y * blockDim.y) + threadIdx.y;
        if (x >= width || y >= height)
        {
            return;
        }

        float sum = 0.0f;
        for (int offset = -radius; offset <= radius; ++offset)
        {
            const int sampleX = clampIndex(x + offset, width);
            sum += source[(y * width) + sampleX] * weights[offset + radius];
        }

        destination[(y * width) + x] = sum;
    }

    __global__ void blurVerticalKernel(
        const float* source,
        float* destination,
        int width,
        int height,
        int radius,
        const float* weights)
    {
        const int x = (blockIdx.x * blockDim.x) + threadIdx.x;
        const int y = (blockIdx.y * blockDim.y) + threadIdx.y;
        if (x >= width || y >= height)
        {
            return;
        }

        float sum = 0.0f;
        for (int offset = -radius; offset <= radius; ++offset)
        {
            const int sampleY = clampIndex(y + offset, height);
            sum += source[(sampleY * width) + x] * weights[offset + radius];
        }

        destination[(y * width) + x] = sum;
    }

    __global__ void highPassAbsKernel(
        const float* source,
        const float* blurred,
        float* output,
        int length)
    {
        const int index = (blockIdx.x * blockDim.x) + threadIdx.x;
        if (index >= length)
        {
            return;
        }

        output[index] = fabsf(source[index] - blurred[index]);
    }

    void computeGaussianWeights(float sigma, int radius, float* weights)
    {
        const int kernelSize = (2 * radius) + 1;
        float sum = 0.0f;
        for (int offset = -radius; offset <= radius; ++offset)
        {
            const float weight = expf(-(static_cast<float>(offset * offset)) / (2.0f * sigma * sigma));
            weights[offset + radius] = weight;
            sum += weight;
        }

        for (int i = 0; i < kernelSize; ++i)
        {
            weights[i] /= sum;
        }
    }

    void freeHighPassBuffers(
        float* source,
        float* temp,
        float* blurred,
        float* output,
        float* weights)
    {
        cudaFree(source);
        cudaFree(temp);
        cudaFree(blurred);
        cudaFree(output);
        cudaFree(weights);
    }

    void freeAll(
        float* target,
        float* other1,
        float* other2,
        float* targetHighPass,
        float* other1HighPass,
        float* other2HighPass,
        uint8_t* outputMask)
    {
        cudaFree(target);
        cudaFree(other1);
        cudaFree(other2);
        cudaFree(targetHighPass);
        cudaFree(other1HighPass);
        cudaFree(other2HighPass);
        cudaFree(outputMask);
    }

    int ensureDevice()
    {
        int deviceCount = 0;
        const cudaError_t error = cudaGetDeviceCount(&deviceCount);
        if (error != cudaSuccess)
        {
            return static_cast<int>(error);
        }

        return deviceCount > 0 ? 0 : -2;
    }

    int copyToDevice(float** device, const float* host, size_t bytes)
    {
        cudaError_t error = cudaMalloc(reinterpret_cast<void**>(device), bytes);
        if (error != cudaSuccess)
        {
            return static_cast<int>(error);
        }

        error = cudaMemcpy(*device, host, bytes, cudaMemcpyHostToDevice);
        return error == cudaSuccess ? 0 : static_cast<int>(error);
    }

    int copyMaskToDevice(uint8_t** device, const uint8_t* host, size_t bytes)
    {
        cudaError_t error = cudaMalloc(reinterpret_cast<void**>(device), bytes);
        if (error != cudaSuccess)
        {
            return static_cast<int>(error);
        }

        error = cudaMemcpy(*device, host, bytes, cudaMemcpyHostToDevice);
        return error == cudaSuccess ? 0 : static_cast<int>(error);
    }
}

PROKUDIN_CUDA_API int ProkudinCudaIsAvailable()
{
    return ensureDevice() == 0 ? 1 : 0;
}

PROKUDIN_CUDA_API int ProkudinCudaDetectDefectMask(
    const float* target,
    const float* other1,
    const float* other2,
    const float* targetHighPass,
    const float* other1HighPass,
    const float* other2HighPass,
    int length,
    double coefficientA,
    double coefficientB,
    double coefficientC,
    float residualThreshold,
    float highPassThreshold,
    float supportMultiplier,
    float supportOffset,
    uint8_t* outputMask)
{
    if (target == nullptr ||
        other1 == nullptr ||
        other2 == nullptr ||
        targetHighPass == nullptr ||
        other1HighPass == nullptr ||
        other2HighPass == nullptr ||
        outputMask == nullptr ||
        length <= 0)
    {
        return -1;
    }

    const int deviceStatus = ensureDevice();
    if (deviceStatus != 0)
    {
        return deviceStatus;
    }

    const size_t floatBytes = static_cast<size_t>(length) * sizeof(float);
    const size_t maskBytes = static_cast<size_t>(length) * sizeof(uint8_t);

    float* deviceTarget = nullptr;
    float* deviceOther1 = nullptr;
    float* deviceOther2 = nullptr;
    float* deviceTargetHighPass = nullptr;
    float* deviceOther1HighPass = nullptr;
    float* deviceOther2HighPass = nullptr;
    uint8_t* deviceOutputMask = nullptr;

    int status = copyToDevice(&deviceTarget, target, floatBytes);
    if (status == 0) status = copyToDevice(&deviceOther1, other1, floatBytes);
    if (status == 0) status = copyToDevice(&deviceOther2, other2, floatBytes);
    if (status == 0) status = copyToDevice(&deviceTargetHighPass, targetHighPass, floatBytes);
    if (status == 0) status = copyToDevice(&deviceOther1HighPass, other1HighPass, floatBytes);
    if (status == 0) status = copyToDevice(&deviceOther2HighPass, other2HighPass, floatBytes);
    if (status == 0)
    {
        const cudaError_t error = cudaMalloc(reinterpret_cast<void**>(&deviceOutputMask), maskBytes);
        status = error == cudaSuccess ? 0 : static_cast<int>(error);
    }

    if (status != 0)
    {
        freeAll(
            deviceTarget,
            deviceOther1,
            deviceOther2,
            deviceTargetHighPass,
            deviceOther1HighPass,
            deviceOther2HighPass,
            deviceOutputMask);
        return status;
    }

    const int blockCount = (length + ThreadsPerBlock - 1) / ThreadsPerBlock;
    detectDefectMaskKernel<<<blockCount, ThreadsPerBlock>>>(
        deviceTarget,
        deviceOther1,
        deviceOther2,
        deviceTargetHighPass,
        deviceOther1HighPass,
        deviceOther2HighPass,
        length,
        coefficientA,
        coefficientB,
        coefficientC,
        residualThreshold,
        highPassThreshold,
        supportMultiplier,
        supportOffset,
        deviceOutputMask);

    cudaError_t error = cudaGetLastError();
    if (error == cudaSuccess)
    {
        error = cudaDeviceSynchronize();
    }

    if (error == cudaSuccess)
    {
        error = cudaMemcpy(outputMask, deviceOutputMask, maskBytes, cudaMemcpyDeviceToHost);
    }

    freeAll(
        deviceTarget,
        deviceOther1,
        deviceOther2,
        deviceTargetHighPass,
        deviceOther1HighPass,
        deviceOther2HighPass,
        deviceOutputMask);

    return error == cudaSuccess ? 0 : static_cast<int>(error);
}

PROKUDIN_CUDA_API int ProkudinCudaPredictMasked(
    const float* target,
    const float* guide1,
    const float* guide2,
    const uint8_t* defectMask,
    int length,
    double coefficientA,
    double coefficientB,
    double coefficientC,
    float* output)
{
    if (target == nullptr ||
        guide1 == nullptr ||
        guide2 == nullptr ||
        defectMask == nullptr ||
        output == nullptr ||
        length <= 0)
    {
        return -1;
    }

    const int deviceStatus = ensureDevice();
    if (deviceStatus != 0)
    {
        return deviceStatus;
    }

    const size_t floatBytes = static_cast<size_t>(length) * sizeof(float);
    const size_t maskBytes = static_cast<size_t>(length) * sizeof(uint8_t);

    float* deviceTarget = nullptr;
    float* deviceGuide1 = nullptr;
    float* deviceGuide2 = nullptr;
    uint8_t* deviceDefectMask = nullptr;
    float* deviceOutput = nullptr;

    int status = copyToDevice(&deviceTarget, target, floatBytes);
    if (status == 0) status = copyToDevice(&deviceGuide1, guide1, floatBytes);
    if (status == 0) status = copyToDevice(&deviceGuide2, guide2, floatBytes);
    if (status == 0) status = copyMaskToDevice(&deviceDefectMask, defectMask, maskBytes);
    if (status == 0)
    {
        const cudaError_t error = cudaMalloc(reinterpret_cast<void**>(&deviceOutput), floatBytes);
        status = error == cudaSuccess ? 0 : static_cast<int>(error);
    }

    if (status != 0)
    {
        cudaFree(deviceTarget);
        cudaFree(deviceGuide1);
        cudaFree(deviceGuide2);
        cudaFree(deviceDefectMask);
        cudaFree(deviceOutput);
        return status;
    }

    const int blockCount = (length + ThreadsPerBlock - 1) / ThreadsPerBlock;
    predictMaskedKernel<<<blockCount, ThreadsPerBlock>>>(
        deviceTarget,
        deviceGuide1,
        deviceGuide2,
        deviceDefectMask,
        length,
        coefficientA,
        coefficientB,
        coefficientC,
        deviceOutput);

    cudaError_t error = cudaGetLastError();
    if (error == cudaSuccess)
    {
        error = cudaDeviceSynchronize();
    }

    if (error == cudaSuccess)
    {
        error = cudaMemcpy(output, deviceOutput, floatBytes, cudaMemcpyDeviceToHost);
    }

    cudaFree(deviceTarget);
    cudaFree(deviceGuide1);
    cudaFree(deviceGuide2);
    cudaFree(deviceDefectMask);
    cudaFree(deviceOutput);

    return error == cudaSuccess ? 0 : static_cast<int>(error);
}

PROKUDIN_CUDA_API int ProkudinCudaHighPassAbs(
    const float* source,
    int width,
    int height,
    float sigma,
    float* output)
{
    if (source == nullptr || output == nullptr || width <= 0 || height <= 0 || sigma <= 0.0f)
    {
        return -1;
    }

    const int deviceStatus = ensureDevice();
    if (deviceStatus != 0)
    {
        return deviceStatus;
    }

    const int length = width * height;
    const size_t floatBytes = static_cast<size_t>(length) * sizeof(float);
    const int radius = static_cast<int>(ceilf(sigma * 3.0f));
    const int kernelSize = (2 * radius) + 1;
    float hostWeights[64] = {};
    if (kernelSize > 64)
    {
        return -1;
    }

    computeGaussianWeights(sigma, radius, hostWeights);

    float* deviceSource = nullptr;
    float* deviceTemp = nullptr;
    float* deviceBlurred = nullptr;
    float* deviceOutput = nullptr;
    float* deviceWeights = nullptr;

    int status = copyToDevice(&deviceSource, source, floatBytes);
    if (status == 0)
    {
        const cudaError_t error = cudaMalloc(reinterpret_cast<void**>(&deviceTemp), floatBytes);
        status = error == cudaSuccess ? 0 : static_cast<int>(error);
    }

    if (status == 0)
    {
        const cudaError_t error = cudaMalloc(reinterpret_cast<void**>(&deviceBlurred), floatBytes);
        status = error == cudaSuccess ? 0 : static_cast<int>(error);
    }

    if (status == 0)
    {
        const cudaError_t error = cudaMalloc(reinterpret_cast<void**>(&deviceOutput), floatBytes);
        status = error == cudaSuccess ? 0 : static_cast<int>(error);
    }

    if (status == 0)
    {
        const cudaError_t error = cudaMalloc(reinterpret_cast<void**>(&deviceWeights), kernelSize * sizeof(float));
        status = error == cudaSuccess ? 0 : static_cast<int>(error);
    }

    if (status != 0)
    {
        freeHighPassBuffers(deviceSource, deviceTemp, deviceBlurred, deviceOutput, deviceWeights);
        return status;
    }

    cudaError_t error = cudaMemcpy(deviceWeights, hostWeights, kernelSize * sizeof(float), cudaMemcpyHostToDevice);
    if (error != cudaSuccess)
    {
        freeHighPassBuffers(deviceSource, deviceTemp, deviceBlurred, deviceOutput, deviceWeights);
        return static_cast<int>(error);
    }

    const dim3 blockSize(16, 16);
    const dim3 gridSize(
        (width + blockSize.x - 1) / blockSize.x,
        (height + blockSize.y - 1) / blockSize.y);

    blurHorizontalKernel<<<gridSize, blockSize>>>(
        deviceSource,
        deviceTemp,
        width,
        height,
        radius,
        deviceWeights);
    error = cudaGetLastError();
    if (error == cudaSuccess)
    {
        blurVerticalKernel<<<gridSize, blockSize>>>(
            deviceTemp,
            deviceBlurred,
            width,
            height,
            radius,
            deviceWeights);
        error = cudaGetLastError();
    }

    if (error == cudaSuccess)
    {
        const int blockCount = (length + ThreadsPerBlock - 1) / ThreadsPerBlock;
        highPassAbsKernel<<<blockCount, ThreadsPerBlock>>>(
            deviceSource,
            deviceBlurred,
            deviceOutput,
            length);
        error = cudaGetLastError();
    }

    if (error == cudaSuccess)
    {
        error = cudaDeviceSynchronize();
    }

    if (error == cudaSuccess)
    {
        error = cudaMemcpy(output, deviceOutput, floatBytes, cudaMemcpyDeviceToHost);
    }

    freeHighPassBuffers(deviceSource, deviceTemp, deviceBlurred, deviceOutput, deviceWeights);
    return error == cudaSuccess ? 0 : static_cast<int>(error);
}
