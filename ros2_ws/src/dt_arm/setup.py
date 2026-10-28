from glob import glob
from setuptools import setup

package_name = 'dt_arm'

setup(
    name=package_name,
    version='0.0.1',
    packages=[package_name],
    data_files=[
        ('share/ament_index/resource_index/packages', ['resource/' + package_name]),
        ('share/' + package_name, ['package.xml']),
        ('share/' + package_name + '/config', glob('config/*.yaml')),
    ],
    install_requires=['setuptools'],
    zip_safe=True,
    maintainer='INDEX',
    maintainer_email='index-team@example.com',
    description='로봇팔 공통 집기 서비스 (arm_N_controller). arm_id 파라미터로 4대 공용',
    license='Apache-2.0',
    tests_require=['pytest'],
    entry_points={
        'console_scripts': [
            'arm_controller_node = dt_arm.arm_controller_node:main',
        ],
    },
)
