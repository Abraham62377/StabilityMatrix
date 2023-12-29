#!/bin/sh

echo "Signing file: $1"

# Setup keychain in CI
if [ -n "$CI" ]; then
    # Turn our base64-encoded certificate back to a regular .p12 file
    
    echo "$MACOS_CERTIFICATE" | base64 --decode -o certificate.p12
    
    # We need to create a new keychain, otherwise using the certificate will prompt
    # with a UI dialog asking for the certificate password, which we can't
    # use in a headless CI environment
    
    security create-keychain -p "$MACOS_CI_KEYCHAIN_PWD" build.keychain 
    security default-keychain -s build.keychain
    security unlock-keychain -p "$MACOS_CI_KEYCHAIN_PWD" build.keychain
    security import certificate.p12 -k build.keychain -P "$MACOS_CERTIFICATE_PWD" -T /usr/bin/codesign
    security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$MACOS_CI_KEYCHAIN_PWD" build.keychain
fi

# Sign all files

ENTITLEMENTS="AppEntitlements.entitlements"

find "$1/Contents/MacOS/"|while read fname; do
    if [[ -f $fname ]]; then
        echo "[INFO] Signing $fname"
        codesign --force --timestamp -s "$MACOS_CERTIFICATE_NAME" --options=runtime --entitlements "$ENTITLEMENTS" "$fname"
    fi
done

echo "[INFO] Signing app file"

codesign --force --timestamp -s "$MACOS_CERTIFICATE_NAME" --options=runtime --entitlements "$ENTITLEMENTS" "$1" -v
