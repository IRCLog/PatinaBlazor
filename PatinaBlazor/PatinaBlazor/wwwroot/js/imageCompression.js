window.imageCompression = {
    compressImage: function (file, maxSizeInMB = 10, quality = 0.8) {
        return new Promise((resolve, reject) => {
            const maxSizeBytes = maxSizeInMB * 1024 * 1024;

            // If file is already small enough, return as-is
            if (file.size <= maxSizeBytes) {
                resolve(file);
                return;
            }

            const canvas = document.createElement('canvas');
            const ctx = canvas.getContext('2d');
            const img = new Image();

            img.onload = function () {
                // Calculate new dimensions to reduce file size
                let { width, height } = img;
                const ratio = Math.min(2048 / width, 2048 / height); // Max 2048px on either side

                if (ratio < 1) {
                    width *= ratio;
                    height *= ratio;
                }

                canvas.width = width;
                canvas.height = height;

                // Draw and compress
                ctx.drawImage(img, 0, 0, width, height);

                canvas.toBlob(
                    (blob) => {
                        if (blob) {
                            // Create a new File object with compressed data
                            const compressedFile = new File([blob], file.name, {
                                type: blob.type,
                                lastModified: Date.now()
                            });
                            resolve(compressedFile);
                        } else {
                            reject(new Error('Image compression failed'));
                        }
                    },
                    file.type,
                    quality
                );
            };

            img.onerror = () => reject(new Error('Failed to load image'));
            img.src = URL.createObjectURL(file);
        });
    },

    compressMultipleImages: async function (files, maxSizeInMB = 10, quality = 0.8) {
        const compressedFiles = [];

        for (let i = 0; i < files.length; i++) {
            try {
                const compressedFile = await this.compressImage(files[i], maxSizeInMB, quality);
                compressedFiles.push(compressedFile);
            } catch (error) {
                console.error('Error compressing image:', files[i].name, error);
                // If compression fails, use original file
                compressedFiles.push(files[i]);
            }
        }

        return compressedFiles;
    },

    // Setup automatic compression for a file input element
    setupFileInputCompression: function (inputElementId, blazorObjectRef, methodName) {
        const inputElement = document.getElementById(inputElementId);
        if (!inputElement) {
            console.error('File input element not found:', inputElementId);
            return;
        }

        inputElement.addEventListener('change', async function (event) {
            const files = Array.from(event.target.files);
            if (files.length === 0) return;

            // Notify Blazor that compression is starting
            blazorObjectRef.invokeMethodAsync('OnCompressionStart');

            try {
                // Only compress image files larger than 3MB
                const compressedFiles = await window.imageCompression.compressMultipleImages(files, 3, 0.8);

                // Update the input element with compressed files
                const dataTransfer = new DataTransfer();
                compressedFiles.forEach(file => dataTransfer.items.add(file));
                inputElement.files = dataTransfer.files;

                // Notify Blazor that compression is complete
                blazorObjectRef.invokeMethodAsync('OnCompressionComplete');

                // Trigger the normal Blazor change event
                inputElement.dispatchEvent(new Event('change'));

            } catch (error) {
                console.error('Compression failed:', error);
                blazorObjectRef.invokeMethodAsync('OnCompressionError', error.toString());
            }
        });
    },

    // Helper function to get file size in MB
    getFileSizeInMB: function (file) {
        return file.size / (1024 * 1024);
    },

    // Helper function to check if file is an image
    isImage: function (file) {
        return file.type.startsWith('image/');
    }
};