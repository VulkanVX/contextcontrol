cmake_minimum_required(VERSION 3.20)

project(contextcontrol_native LANGUAGES CXX)

add_executable(ccnative
    src/main.cpp
)

target_compile_features(ccnative PRIVATE cxx_std_17)

if(MSVC)
    target_compile_definitions(ccnative PRIVATE _CRT_SECURE_NO_WARNINGS)
    target_compile_options(ccnative PRIVATE /W4 /permissive-)
else()
    target_compile_options(ccnative PRIVATE -Wall -Wextra -Wpedantic)
endif()
