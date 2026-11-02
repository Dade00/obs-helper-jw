# Build plugin OBS Helper JW — C++ plugin nativo OBS
# OBS SDK costruito da sorgente in obs-sdk/obs-studio/

cmake_minimum_required(VERSION 3.16)
project(ObsHelperJw VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Percorsi OBS SDK (costruiti da sorgente)
set(OBS_SDK_DIR "D:/DZWebDesign/obs_helper_jw/obs-sdk/obs-studio" CACHE PATH "Percorso sorgenti OBS Studio")
set(OBS_BUILD_DIR "D:/DZWebDesign/obs_helper_jw/obs-sdk/build" CACHE PATH "Percorso build OBS Studio")

include_directories(${OBS_SDK_DIR}/libobs)
include_directories(${OBS_SDK_DIR}/frontend/api)
include_directories(${OBS_SDK_DIR}/libobs-winrt)
include_directories(${OBS_BUILD_DIR})
include_directories(${OBS_BUILD_DIR}/config)
include_directories(${OBS_BUILD_DIR}/deps/glad)
include_directories(${CMAKE_SOURCE_DIR}/src)

add_library(obs-helper-jw MODULE
    src/obs_helper_jw.cpp
    src/detector/detector_impl.cpp
    src/state_machine/state_machine.cpp
    src/obs_adapter/obs_adapter.cpp
    src/settings/settings.cpp
)

target_link_libraries(obs-helper-jw PRIVATE
    ${OBS_BUILD_DIR}/libobs/Release/obs.lib
    ${OBS_BUILD_DIR}/frontend/api/Release/obs-frontend-api.lib
    runtimeobject.lib
)

# Test stato macchina (compilabile senza OBS e senza WinRT)
add_executable(state_machine_test tests/state_machine_test.cpp
    src/state_machine/state_machine.cpp
)

# Output: obs-helper-jw.dll (plugin OBS per Windows x64)
set_target_properties(obs-helper-jw PROPERTIES PREFIX "" SUFFIX ".dll")
