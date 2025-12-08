#!/bin/bash

echo "🚀 Building Reqnroll Recorder..."

# Create build directories
mkdir -p build/chrome
mkdir -p build/firefox

echo "📦 Building Chrome version..."
# Copy all files to Chrome build
cp -r *.js *.html *.css icons build/chrome/ 2>/dev/null || true
cp manifest-chrome.json build/chrome/manifest.json

echo "🦊 Building Firefox version..."
# Copy all files to Firefox build
cp -r *.js *.html *.css icons build/firefox/ 2>/dev/null || true
cp manifest-firefox.json build/firefox/manifest.json

echo "📦 Creating ZIP packages..."
cd build/chrome && zip -r ../reqnroll-recorder-chrome-v1.0.0.zip * && cd ../..
cd build/firefox && zip -r ../reqnroll-recorder-firefox-v1.0.0.zip * && cd ../..

echo "✅ Build complete!"
echo ""
echo "📁 Chrome package: build/reqnroll-recorder-chrome-v1.0.0.zip"
echo "📁 Firefox package: build/reqnroll-recorder-firefox-v1.0.0.zip"
echo ""
echo "🎯 Next steps:"
echo "   Chrome: Upload to Chrome Web Store"
echo "   Firefox: Upload to Firefox Add-ons"

