from glob import glob
from setuptools import setup

package_name = 'dt_fake_unity'

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
    description='Unity 없이 ROS2 노드를 테스트하기 위한 가짜 노드 (Unity, palletizer, agv_1_controller)',
    license='Apache-2.0',
    tests_require=['pytest'],
    entry_points={
        'console_scripts': [
            'fake_unity_node = dt_fake_unity.fake_unity_node:main',
            'fake_panel = dt_fake_unity.fake_panel:main',
            'fake_palletizer = dt_fake_unity.fake_palletizer:main',
            'fake_agv_controller = dt_fake_unity.fake_agv_controller:main',
        ],
    },
)
